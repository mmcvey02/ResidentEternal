#include "config.hpp"
#include "common/json_lite.hpp"
#include <algorithm>
#include <fstream>
#include <sstream>

namespace rcsk
{
	std::string Config::to_json() const
	{
		std::string s = "{\n";
		s += "  \"port\": " + std::to_string(port) + ",\n";
		s += "  \"camHz\": ";
		json::append_number(s, cam_hz);
		s += ",\n  \"fovIsHorizontal\": ";
		s += fov_is_horizontal ? "true" : "false";
		s += ",\n  \"bodycamEvery\": " + std::to_string(bodycam_every) + ",\n";
		s += "  \"bodycamWidth\": " + std::to_string(bodycam_width) + "\n}\n";
		return s;
	}

	Config Config::load_or_create(const std::filesystem::path &file, std::string &log)
	{
		Config c;
		std::ifstream in(file, std::ios::binary);
		if (!in)
		{
			std::error_code ec;
			std::filesystem::create_directories(file.parent_path(), ec);
			std::ofstream(file, std::ios::binary) << c.to_json();
			log = "wrote default config " + file.string();
			return c;
		}
		std::stringstream ss;
		ss << in.rdbuf();
		auto v = json::parse(ss.str());
		if (!v || !v->is_object())
		{
			log = "config.json is not valid JSON; using defaults";
			return c;
		}
		const double port = v->number("port", c.port);
		if (port > 1024 && port < 65536)
			c.port = uint16_t(port);
		c.cam_hz = std::max(1.0, std::min(240.0, v->number("camHz", c.cam_hz)));
		c.fov_is_horizontal = v->boolean("fovIsHorizontal", c.fov_is_horizontal);
		c.bodycam_every = int(std::max(0.0, v->number("bodycamEvery", c.bodycam_every)));
		c.bodycam_width = int(std::max(64.0, std::min(1920.0, v->number("bodycamWidth", c.bodycam_width))));
		log = "loaded " + file.string();
		return c;
	}
}
