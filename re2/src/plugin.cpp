// Raccoon City Skylines: the Resident Evil 2 half. One DLL, two roles:
//  - a REFramework plugin (reframework/plugins/RaccoonSkylines.dll): reads RE2's camera every frame, talks to
//    Cities: Skylines over the link, and trades state with raccoon_skylines.lua through files;
//  - a ReShade add-on (compositor.cpp): draws the city into RE2's sky and sends RE2's picture back as the bodycam.
#include "common/json_lite.hpp"
#include "common/link.hpp"
#include "compositor.hpp"
#include "config.hpp"
#include "re_camera.hpp"
#include <algorithm>
#include <chrono>
#include <cmath>
#include <deque>
#include <filesystem>
#include <fstream>
#include <memory>
#include <mutex>
#include <sstream>
#include <string>
#include <windows.h>
#include <psapi.h>
#include <cstdio>
#include <reframework/API.hpp>

using reframework::API;
namespace fs = std::filesystem;

namespace
{
	constexpr int kCamKind = 1;
	constexpr size_t kKeepEvents = 32;

	HMODULE g_module = nullptr;
	rcsk::Config g_cfg;
	std::unique_ptr<rcsk::Link> g_link;
	fs::path g_dataDir;
	uint32_t g_generation = 0;
	int64_t g_frame = 0;
	std::chrono::steady_clock::time_point g_nextCam{}, g_nextBridge{};

	// Last city state and recent city events, in raw JSON, for the Lua side.
	std::string g_cityRaw = "null";
	std::deque<std::string> g_cityEvents;
	rcsk::compositor::CityLook g_look;

	// RE2 events from Lua that were already forwarded.
	double g_lastForwardedEvent = 0;
	fs::file_time_type g_re2Mtime{};

	std::mutex g_logLock;

	/// Logs to REFramework's log and to reframework/data/raccoon_skylines/plugin.log (ours alone, easy to send).
	void log_info(const std::string &s)
	{
		API::get()->log_info("[RaccoonSkylines] %s", s.c_str());
		if (g_dataDir.empty())
			return;
		std::lock_guard<std::mutex> g(g_logLock);
		std::ofstream out(g_dataDir / "plugin.log", std::ios::app);
		SYSTEMTIME t;
		GetLocalTime(&t);
		char stamp[32];
		std::snprintf(stamp, sizeof(stamp), "%02d:%02d:%02d.%03d ", t.wHour, t.wMinute, t.wSecond, t.wMilliseconds);
		out << stamp << s << "\n";
	}

	bool g_reshadeRegistered = false;
	bool g_sawBeginRendering = false;
	DWORD g_nextRegisterLog = 0;

	/// Registers with ReShade, logging the outcome (and why it keeps failing, every 10 seconds).
	void register_with_reshade(const char *from)
	{
		if (g_reshadeRegistered)
			return;
		if (rcsk::compositor::try_register(g_module))
		{
			g_reshadeRegistered = true;
			log_info(std::string("registered with ReShade (from ") + from + ")");
			return;
		}
		const DWORD now = GetTickCount();
		if (now < g_nextRegisterLog)
			return;
		g_nextRegisterLog = now + 10000;
		// Find what ReShade module, if any, is in the process.
		HMODULE modules[1024];
		DWORD needed = 0;
		std::string found = "none";
		if (EnumProcessModules(GetCurrentProcess(), modules, sizeof(modules), &needed))
			for (DWORD i = 0; i < needed / sizeof(HMODULE); ++i)
				if (GetProcAddress(modules[i], "ReShadeRegisterAddon") != nullptr)
				{
					wchar_t name[MAX_PATH] = {};
					GetModuleFileNameW(modules[i], name, MAX_PATH);
					found = fs::path(name).filename().string();
				}
		log_info(std::string("ReShade add-on registration failed (from ") + from + "); module exporting ReShadeRegisterAddon: " + found +
			(found == "none" ? " -> install ReShade *with full add-on support*" : " -> ReShade refused this add-on's API version"));
	}

	fs::path game_dir()
	{
		wchar_t path[MAX_PATH] = {};
		GetModuleFileNameW(nullptr, path, MAX_PATH);
		return fs::path(path).parent_path();
	}

	/// Writes a file so a reader never sees half of it: temp file, then an atomic replace.
	void write_atomic(const fs::path &file, const std::string &content)
	{
		const fs::path tmp = file.wstring() + L".tmp";
		{
			std::ofstream out(tmp, std::ios::binary | std::ios::trunc);
			if (!out)
				return;
			out << content;
		}
		MoveFileExW(tmp.c_str(), file.c_str(), MOVEFILE_REPLACE_EXISTING);
	}

	void handle_city_message(const std::string &line)
	{
		auto m = rcsk::json::parse(line);
		if (!m || !m->is_object())
			return;
		const std::string t = m->string("t");
		if (t == "hello")
			log_info("city says hello: " + m->string("city"));
		else if (t == "city")
		{
			g_cityRaw = line;
			g_look.infection = float(std::clamp(m->number("inf", 0.0), 0.0, 1.0));
			g_look.power = m->boolean("power", true);
			g_look.daylight = float(std::clamp(m->number("daylight", 1.0), 0.0, 1.0));
		}
		else if (t == "ev")
		{
			g_cityEvents.push_back(line);
			while (g_cityEvents.size() > kKeepEvents)
				g_cityEvents.pop_front();
			if (m->string("k") == "wave")
			{
				const rcsk::json::Value *d = m->get("d");
				rcsk::compositor::pulse(float(d ? d->number("strength", 1.0) : 1.0));
			}
		}
	}

	void send_camera()
	{
		const auto now = std::chrono::steady_clock::now();
		if (now < g_nextCam)
			return;
		g_nextCam = now + std::chrono::microseconds(int64_t(1e6 / g_cfg.cam_hz));
		rcsk::CameraPose cam;
		if (!rcsk::read_camera(cam))
			return;
		uint32_t w = 0, h = 0;
		rcsk::compositor::backbuffer_size(w, h);
		if (w == 0 || h == 0)
		{
			w = 1920;
			h = 1080;
		}
		float fov = cam.fov;
		if (g_cfg.fov_is_horizontal)
			fov = float(2.0 * std::atan(std::tan(fov * 3.14159265 / 360.0) * double(h) / double(w)) * 180.0 / 3.14159265);
		std::string s = "{\"t\":\"cam\",\"f\":" + std::to_string(++g_frame) + ",\"p\":[";
		for (int i = 0; i < 3; ++i)
		{
			if (i)
				s += ',';
			rcsk::json::append_number(s, cam.pos[i]);
		}
		s += "],\"q\":[";
		for (int i = 0; i < 4; ++i)
		{
			if (i)
				s += ',';
			rcsk::json::append_number(s, cam.rot[i]);
		}
		s += "],\"fov\":";
		rcsk::json::append_number(s, fov);
		s += ",\"w\":" + std::to_string(w) + ",\"h\":" + std::to_string(h) + "}";
		g_link->send_latest(std::move(s), kCamKind);
	}

	/// Four times a second: hand city state to Lua, forward Lua's player state and new events to the city.
	void file_bridge()
	{
		const auto now = std::chrono::steady_clock::now();
		if (now < g_nextBridge)
			return;
		g_nextBridge = now + std::chrono::milliseconds(250);

		std::string out = "{\"link\":";
		out += g_link->connected() ? "true" : "false";
		out += ",\"city\":" + g_cityRaw + ",\"events\":[";
		for (size_t i = 0; i < g_cityEvents.size(); ++i)
		{
			if (i)
				out += ',';
			out += g_cityEvents[i];
		}
		out += "]}";
		write_atomic(g_dataDir / "city.json", out);

		const fs::path re2 = g_dataDir / "re2.json";
		std::error_code ec;
		const auto mtime = fs::last_write_time(re2, ec);
		if (ec || mtime == g_re2Mtime)
			return;
		std::ifstream in(re2, std::ios::binary);
		std::stringstream ss;
		ss << in.rdbuf();
		auto v = rcsk::json::parse(ss.str());
		if (!v || !v->is_object())
			return; // Lua may be mid-write; try again next tick
		g_re2Mtime = mtime;
		if (const rcsk::json::Value *player = v->get("player"); player && player->is_object() && g_link->connected())
			g_link->send_latest(rcsk::json::dump(*player), 2);
		if (const rcsk::json::Value *events = v->get("events"); events && events->is_array())
			for (const rcsk::json::Value &e : events->arr)
			{
				const double id = e.number("id", 0);
				if (id <= g_lastForwardedEvent)
					continue;
				g_lastForwardedEvent = id;
				if (g_link->connected())
					g_link->send(rcsk::json::dump(e));
			}
	}

	void on_present()
	{
		register_with_reshade("present");
	}

	void on_begin_rendering()
	{
		if (!g_sawBeginRendering)
		{
			g_sawBeginRendering = true;
			log_info("first BeginRendering callback");
		}
		register_with_reshade("BeginRendering");
		if (!g_link)
			return;
		if (g_link->generation() != g_generation && g_link->connected())
		{
			g_generation = g_link->generation();
			g_link->send("{\"t\":\"hello\",\"side\":\"re2\",\"v\":1}");
		}
		for (const std::string &line : g_link->drain())
			handle_city_message(line);
		g_look.link = g_link->connected();
		rcsk::compositor::set_city(g_look);
		if (g_link->connected())
			send_camera();
		file_bridge();
	}
}

// How ReShade labels the add-on in its Add-ons tab.
extern "C"
{
	__declspec(dllexport) const char *NAME = "RaccoonSkylines";
	__declspec(dllexport) const char *DESCRIPTION = "Draws your Cities: Skylines city into RE2's sky and sends RE2's picture back as the bodycam.";
}

extern "C" __declspec(dllexport) void reframework_plugin_required_version(REFrameworkPluginVersion *version)
{
	version->major = REFRAMEWORK_PLUGIN_VERSION_MAJOR;
	version->minor = REFRAMEWORK_PLUGIN_VERSION_MINOR;
	version->patch = REFRAMEWORK_PLUGIN_VERSION_PATCH;
	// No game_name: REFramework compares it to its own internal name, and a mismatch would refuse the plugin.
}

extern "C" __declspec(dllexport) bool reframework_plugin_initialize(const REFrameworkPluginInitializeParam *param)
{
	API::initialize(param);
	g_dataDir = game_dir() / "reframework" / "data" / "raccoon_skylines";
	std::error_code ec;
	fs::create_directories(g_dataDir, ec);
	std::string msg;
	log_info("---- RaccoonSkylines plugin initializing (REFramework plugin API " + std::to_string(REFRAMEWORK_PLUGIN_VERSION_MAJOR) + "." +
		std::to_string(REFRAMEWORK_PLUGIN_VERSION_MINOR) + ")");
	g_cfg = rcsk::Config::load_or_create(g_dataDir / "config.json", msg);
	log_info(msg);
	rcsk::compositor::set_bodycam(g_cfg.bodycam_every, g_cfg.bodycam_width);
	g_link = std::make_unique<rcsk::Link>(g_cfg.port);
	g_link->log = [](const std::string &s) { log_info(s); };
	g_link->start();
	const bool hooked = param->functions->on_pre_application_entry("BeginRendering", on_begin_rendering);
	const bool presentHooked = param->functions->on_present(on_present);
	log_info(std::string("BeginRendering callback ") + (hooked ? "installed" : "REFUSED") + ", present callback " + (presentHooked ? "installed" : "REFUSED"));
	register_with_reshade("initialize");
	log_info("loaded; looking for Cities: Skylines on 127.0.0.1:" + std::to_string(g_cfg.port));
	return true;
}

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID)
{
	if (reason == DLL_PROCESS_ATTACH)
	{
		g_module = module;
		DisableThreadLibraryCalls(module);
	}
	else if (reason == DLL_PROCESS_DETACH)
	{
		// The process is going away: don't join threads under the loader lock, just let them die with it.
		if (g_link)
			(void)g_link.release();
		rcsk::compositor::unregister(module);
	}
	return TRUE;
}
