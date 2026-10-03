// The plugin's settings, read from reframework/data/raccoon_skylines/config.json (written with defaults if missing).
#pragma once
#include <cstdint>
#include <filesystem>
#include <string>

namespace rcsk
{
	struct Config
	{
		uint16_t port = 25600;
		/// Camera messages per second at most.
		double cam_hz = 60.0;
		/// RE Engine cameras report FOV; set true if your build reports it horizontally (the city wants vertical).
		bool fov_is_horizontal = false;
		/// Publish a bodycam frame to Cities: Skylines every N presented frames (0 = off).
		int bodycam_every = 4;
		int bodycam_width = 480;

		static Config load_or_create(const std::filesystem::path &file, std::string &log);
		std::string to_json() const;
	};
}
