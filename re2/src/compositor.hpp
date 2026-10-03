// The ReShade add-on half of the plugin: draws the city into RE2's sky, applies the outbreak's look, and publishes
// RE2's picture back to Cities: Skylines as the bodycam.
#pragma once
#include <cstdint>

namespace rcsk::compositor
{
	/// Registers with ReShade if it is loaded and supports add-ons. Cheap to call every frame until it succeeds.
	bool try_register(void *module);
	void unregister(void *module);

	struct CityLook
	{
		bool link = false;
		float infection = 0.0f; // anchor district, 0..1
		bool power = true;
		float daylight = 1.0f;
	};
	void set_city(const CityLook &look);
	/// A short pulse in the picture (an outbreak wave hit).
	void pulse(float strength);
	void set_bodycam(int every_n_frames, int width);
	/// RE2's back buffer size as ReShade last saw it (0 until the first frame).
	void backbuffer_size(uint32_t &w, uint32_t &h);
}
