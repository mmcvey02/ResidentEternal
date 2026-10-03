#include "compositor.hpp"
#include "common/frames.hpp"
#include <algorithm>
#include <atomic>
#include <cmath>
#include <mutex>
#include <vector>
#include <windows.h>
#include <reshade.hpp>

using namespace reshade::api;

namespace rcsk::compositor
{
	namespace
	{
		constexpr const char *kEffect = "RaccoonSkylines.fx";

		std::atomic<bool> g_registered{false};
		std::atomic<uint32_t> g_bbw{0}, g_bbh{0};
		std::mutex g_lookLock;
		CityLook g_look;
		std::atomic<float> g_pulse{0.0f};
		std::atomic<int> g_bodycamEvery{4}, g_bodycamWidth{480};

		FrameReader g_city;
		resource g_tex{0};
		resource_view g_srv{0};
		uint32_t g_texW = 0, g_texH = 0;
		bool g_hasFrame = false;
		FrameInfo g_info;
		std::vector<uint8_t> g_pixels;
		float g_darkness = 0.0f;
		DWORD g_lastTick = 0;

		FrameWriter g_bodycam;
		bool g_bodycamFailed = false;
		uint64_t g_presented = 0;
		std::vector<uint8_t> g_shot, g_small;

		void destroy_texture(device *dev)
		{
			if (g_srv.handle)
				dev->destroy_resource_view(g_srv);
			if (g_tex.handle)
				dev->destroy_resource(g_tex);
			g_srv = {0};
			g_tex = {0};
			g_texW = g_texH = 0;
			g_hasFrame = false;
		}

		void bind(effect_runtime *runtime)
		{
			runtime->update_texture_bindings("RCSK_CITY", g_srv, g_srv);
		}

		void upload(effect_runtime *runtime)
		{
			if (!g_city.ensure("City"))
				return;
			FrameInfo info;
			if (!g_city.latest(info, g_pixels))
				return;
			device *dev = runtime->get_device();
			if (info.width != g_texW || info.height != g_texH)
			{
				destroy_texture(dev);
				if (!dev->create_resource(resource_desc(info.width, info.height, 1, 1, format::r8g8b8a8_unorm, 1, memory_heap::default_,
											  resource_usage::shader_resource | resource_usage::copy_dest),
						nullptr, resource_usage::shader_resource, &g_tex) ||
					!dev->create_resource_view(g_tex, resource_usage::shader_resource, resource_view_desc(format::r8g8b8a8_unorm), &g_srv))
				{
					destroy_texture(dev);
					return;
				}
				g_texW = info.width;
				g_texH = info.height;
				bind(runtime);
			}
			subresource_data data;
			data.data = g_pixels.data();
			data.row_pitch = info.width * 4;
			data.slice_pitch = info.width * info.height * 4;
			dev->update_texture_region(data, g_tex, 0);
			g_info = info;
			g_hasFrame = true;
		}

		template <typename... T>
		void set_float(effect_runtime *rt, const char *name, T... v)
		{
			const effect_uniform_variable u = rt->find_uniform_variable(kEffect, name);
			if (u.handle != 0)
				rt->set_uniform_value_float(u, float(v)...);
		}

		void on_begin_effects(effect_runtime *runtime, command_list *, resource_view, resource_view)
		{
			uint32_t w = 0, h = 0;
			runtime->get_screenshot_width_and_height(&w, &h);
			g_bbw = w;
			g_bbh = h;

			CityLook look;
			{
				std::lock_guard<std::mutex> g(g_lookLock);
				look = g_look;
			}
			if (look.link)
				upload(runtime);

			// Blackouts fade in and out over about a second; pulses decay over half a second.
			const DWORD now = GetTickCount();
			const float dt = g_lastTick ? std::min(0.1f, (now - g_lastTick) / 1000.0f) : 0.0f;
			g_lastTick = now;
			const float target = look.link && !look.power ? 1.0f : 0.0f;
			g_darkness += (target - g_darkness) * std::min(1.0f, dt * 2.0f);
			const float pulse = g_pulse.load();
			g_pulse = std::max(0.0f, pulse - dt * 2.0f);

			const effect_uniform_variable active = runtime->find_uniform_variable(kEffect, "CityActive");
			if (active.handle != 0)
				runtime->set_uniform_value_bool(active, look.link && g_hasFrame);
			if (const effect_uniform_variable u = runtime->find_uniform_variable(kEffect, "CityFlags"); u.handle != 0)
				runtime->set_uniform_value_int(u, int(g_info.flags));
			set_float(runtime, "CityFov", g_info.fov, g_texW ? float(g_texW) / float(std::max(1u, g_texH)) : 1.7777f);
			set_float(runtime, "Infection", look.link ? look.infection : 0.0f);
			set_float(runtime, "Darkness", g_darkness);
			set_float(runtime, "Daylight", look.daylight);
			set_float(runtime, "Pulse", pulse);
		}

		/// Box-downscales RE2's RGBA picture to the bodycam width and publishes it.
		void publish_bodycam(effect_runtime *runtime)
		{
			const int every = g_bodycamEvery.load();
			if (every <= 0 || g_bodycamFailed || (++g_presented % uint64_t(every)) != 0)
				return;
			uint32_t w = 0, h = 0;
			runtime->get_screenshot_width_and_height(&w, &h);
			if (w == 0 || h == 0)
				return;
			const uint32_t ow = uint32_t(std::min<int>(g_bodycamWidth.load(), int(w)));
			const uint32_t oh = std::max(1u, uint32_t(uint64_t(h) * ow / w));
			if (!g_bodycam.is_open() && !g_bodycam.open("Bodycam", 1920, 1080))
			{
				g_bodycamFailed = true;
				reshade::log::message(reshade::log::level::warning, "RaccoonSkylines: could not create the bodycam mapping");
				return;
			}
			if (ow > g_bodycam.max_width() || oh > g_bodycam.max_height())
				return;
			g_shot.resize(size_t(w) * h * 4);
			if (!runtime->capture_screenshot(g_shot.data()))
				return;
			g_small.assign(size_t(ow) * oh * 4, 0);
			for (uint32_t y = 0; y < oh; ++y)
			{
				const uint32_t y0 = y * h / oh, y1 = std::max(y0 + 1, (y + 1) * h / oh);
				for (uint32_t x = 0; x < ow; ++x)
				{
					const uint32_t x0 = x * w / ow, x1 = std::max(x0 + 1, (x + 1) * w / ow);
					uint32_t acc[4] = {0, 0, 0, 0}, n = 0;
					for (uint32_t sy = y0; sy < y1; sy += 2)
						for (uint32_t sx = x0; sx < x1; sx += 2, ++n)
						{
							const uint8_t *p = &g_shot[(size_t(sy) * w + sx) * 4];
							acc[0] += p[0];
							acc[1] += p[1];
							acc[2] += p[2];
						}
					uint8_t *o = &g_small[(size_t(y) * ow + x) * 4];
					o[0] = uint8_t(acc[0] / std::max(1u, n));
					o[1] = uint8_t(acc[1] / std::max(1u, n));
					o[2] = uint8_t(acc[2] / std::max(1u, n));
					o[3] = 255;
				}
			}
			FrameInfo info;
			info.frame = int64_t(g_presented);
			info.width = ow;
			info.height = oh;
			g_bodycam.write(info, g_small.data());
		}

		void on_present(effect_runtime *runtime)
		{
			publish_bodycam(runtime);
		}

		void on_reloaded_effects(effect_runtime *runtime)
		{
			if (g_srv.handle)
				bind(runtime);
		}

		void on_destroy_effect_runtime(effect_runtime *runtime)
		{
			destroy_texture(runtime->get_device());
		}
	}

	bool try_register(void *module)
	{
		if (g_registered)
			return true;
		if (!reshade::register_addon(module))
			return false;
		reshade::register_event<reshade::addon_event::reshade_begin_effects>(on_begin_effects);
		reshade::register_event<reshade::addon_event::reshade_present>(on_present);
		reshade::register_event<reshade::addon_event::reshade_reloaded_effects>(on_reloaded_effects);
		reshade::register_event<reshade::addon_event::destroy_effect_runtime>(on_destroy_effect_runtime);
		g_registered = true;
		reshade::log::message(reshade::log::level::info, "RaccoonSkylines: registered with ReShade");
		return true;
	}

	void unregister(void *module)
	{
		if (g_registered.exchange(false))
			reshade::unregister_addon(module);
	}

	void set_city(const CityLook &look)
	{
		std::lock_guard<std::mutex> g(g_lookLock);
		g_look = look;
	}

	void pulse(float strength)
	{
		g_pulse = std::max(g_pulse.load(), std::clamp(strength, 0.0f, 1.0f));
	}

	void set_bodycam(int every_n_frames, int width)
	{
		g_bodycamEvery = every_n_frames;
		g_bodycamWidth = width;
	}

	void backbuffer_size(uint32_t &w, uint32_t &h)
	{
		w = g_bbw.load();
		h = g_bbh.load();
	}
}
