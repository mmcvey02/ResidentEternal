// Tests for the portable half of the RE2 plugin (json_lite, frames, link), built with any C++20 compiler.
//   test_common unit                 the unit tests
//   test_common writer <name> <sec>  publishes test frames (the bodycam direction)
//   test_common client <port> <sec>  acts as RE2 against a city (the Python fake or the C# LinkServer under Mono),
//                                    using the plugin's own Link and FrameReader; prints a summary, exit 0 on success
#include "../src/common/frames.hpp"
#include "../src/common/json_lite.hpp"
#include "../src/common/link.hpp"
#include <chrono>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>
#include <thread>
#include <unistd.h>

using namespace rcsk;

static int g_fail = 0, g_pass = 0;
#define CHECK(cond, what)                                   \
	do                                                      \
	{                                                       \
		if (cond)                                           \
			++g_pass;                                       \
		else                                                \
		{                                                   \
			++g_fail;                                       \
			std::printf("FAIL %s:%d: %s\n", __FILE__, __LINE__, what); \
		}                                                   \
	} while (0)

static void json_tests()
{
	auto v = json::parse(R"({"t":"city","inf":0.25,"power":false,"district":"Up\"towné","p":[1,-2.5,3e2],"n":null,"o":{"k":[]}})");
	CHECK(v && v->is_object(), "parses an object");
	CHECK(v->string("t") == "city", "string member");
	CHECK(v->number("inf") == 0.25, "number member");
	CHECK(!v->boolean("power", true), "bool member");
	CHECK(v->string("district") == "Up\"town\xC3\xA9", "escapes and \\u to UTF-8");
	double p[3];
	CHECK(v->numbers("p", p, 3) && p[1] == -2.5 && p[2] == 300, "numeric array");
	CHECK(!v->numbers("p", p, 4), "short array refused");
	CHECK(v->get("n") && v->get("n")->type == json::Value::Type::Null, "null");
	CHECK(v->get("missing") == nullptr && v->number("missing", 7) == 7, "fallbacks");
	auto again = json::parse(json::dump(*v));
	CHECK(again && json::dump(*again) == json::dump(*v), "dump round trips");
	CHECK(!json::parse("{\"t\":1,}"), "trailing comma refused");
	CHECK(!json::parse("{} x"), "trailing junk refused");
	CHECK(!json::parse("\"unterminated"), "unterminated string refused");
	std::string deep(200, '[');
	CHECK(!json::parse(deep), "deep nesting refused");
	auto emoji = json::parse(R"("🧟")");
	CHECK(emoji && emoji->str == "\xF0\x9F\xA7\x9F", "surrogate pairs");
	std::string n;
	json::append_number(n, NAN);
	CHECK(n == "null", "NaN is null");
}

static void frame_tests()
{
	const std::string name = "CppTest" + std::to_string(getpid());
	FrameReader r;
	CHECK(!r.ensure(name), "no mapping yet");
	FrameWriter w;
	CHECK(w.open(name, 64, 32, 3), "writer opens");
	std::vector<uint8_t> px(16 * 8 * 4);
	for (size_t i = 0; i < px.size(); ++i)
		px[i] = uint8_t(i);
	for (int k = 1; k <= 4; ++k)
	{
		px[0] = uint8_t(k);
		FrameInfo f;
		f.frame = k;
		f.host_frame = 1000 + k;
		f.width = 16;
		f.height = 8;
		f.flags = kFlagBottomUp;
		f.infection = 0.5f;
		CHECK(w.write(f, px.data()), "write");
	}
	FrameInfo big;
	big.width = 65;
	big.height = 1;
	CHECK(!w.write(big, px.data()), "oversized frame refused");
	FrameReader r2;
	// ensure() rate-limits retries to once a second; a fresh reader tries immediately.
	CHECK(r2.ensure(name), "reader opens");
	FrameInfo got;
	std::vector<uint8_t> out;
	CHECK(r2.latest(got, out), "reads newest");
	CHECK(got.frame == 4 && got.host_frame == 1004 && got.width == 16 && got.height == 8 && got.flags == kFlagBottomUp, "metadata");
	CHECK(out.size() == px.size() && out[0] == 4 && out[100] == 100 && got.infection == 0.5f, "pixels");
	CHECK(!r2.latest(got, out), "no repeat");
	r2.close();
	std::remove(("/dev/shm/RaccoonSkylines." + name).c_str());
}

static int client(int port, double seconds)
{
	Link link{static_cast<uint16_t>(port)};
	link.log = [](const std::string &s) { std::printf("%s\n", s.c_str()); };
	link.start();
	FrameReader reader;
	uint32_t gen = 0;
	int city = 0, waves = 0, frames = 0, hellos = 0;
	int64_t last_host_frame = 0;
	double last_inf = -1, first_inf = -1;
	const auto start = std::chrono::steady_clock::now();
	int f = 0, ev = 0;
	bool sent_kills = false, sent_tyrant = false;
	while (true)
	{
		const double t = std::chrono::duration<double>(std::chrono::steady_clock::now() - start).count();
		if (t > seconds)
			break;
		if (link.generation() != gen && link.connected())
		{
			gen = link.generation();
			link.send(R"({"t":"hello","side":"re2","v":1})");
		}
		if (link.connected())
		{
			++f;
			const double yaw = t * 0.5;
			char buf[256];
			std::snprintf(buf, sizeof(buf), R"({"t":"cam","f":%d,"p":[%.3f,1.6,%.3f],"q":[0,%.5f,0,%.5f],"fov":60,"w":1920,"h":1080})", f,
				std::sin(t) * 2, std::cos(t) * 2, std::sin(yaw / 2), std::cos(yaw / 2));
			link.send_latest(buf, 1);
			if (t > 1.0 && !sent_kills)
			{
				sent_kills = true;
				for (int k = 0; k < 5; ++k)
					link.send("{\"t\":\"ev\",\"id\":" + std::to_string(++ev) + ",\"k\":\"kill\",\"d\":{\"enemy\":\"em0000\"}}");
			}
			if (t > 1.5 && !sent_tyrant)
			{
				sent_tyrant = true;
				link.send("{\"t\":\"ev\",\"id\":" + std::to_string(++ev) + ",\"k\":\"tyrant\",\"d\":{}}");
			}
		}
		for (const auto &line : link.drain())
		{
			auto m = json::parse(line);
			if (!m || !m->is_object())
				continue;
			const std::string type = m->string("t");
			if (type == "hello")
				++hellos;
			else if (type == "city")
			{
				++city;
				last_inf = m->number("inf", -1);
				if (first_inf < 0)
					first_inf = last_inf;
			}
			else if (type == "ev" && m->string("k") == "wave")
				++waves;
		}
		if (reader.ensure("City"))
		{
			FrameInfo info;
			std::vector<uint8_t> px;
			if (reader.latest(info, px))
			{
				++frames;
				last_host_frame = info.host_frame;
			}
		}
		std::this_thread::sleep_for(std::chrono::milliseconds(16));
	}
	link.stop();
	std::printf("{\"hellos\":%d,\"city\":%d,\"waves\":%d,\"frames\":%d,\"lastHostFrame\":%lld,\"firstInf\":%.4f,\"lastInf\":%.4f}\n",
		hellos, city, waves, frames, (long long)last_host_frame, first_inf, last_inf);
	return hellos >= 1 && city >= 4 && waves >= 1 && frames >= 1 && last_host_frame > 0 ? 0 : 1;
}

/// Publishes numbered test frames into a mapping (the bodycam direction: RE2 -> Cities: Skylines).
static int writer(const char *name, double seconds)
{
	FrameWriter w;
	if (!w.open(name, 480, 270))
		return 1;
	std::vector<uint8_t> px(480 * 270 * 4, 200);
	const auto start = std::chrono::steady_clock::now();
	int64_t n = 0;
	while (std::chrono::duration<double>(std::chrono::steady_clock::now() - start).count() < seconds)
	{
		FrameInfo f;
		f.frame = ++n;
		f.width = 480;
		f.height = 270;
		px[0] = uint8_t(n);
		w.write(f, px.data());
		std::this_thread::sleep_for(std::chrono::milliseconds(33));
	}
	std::printf("{\"written\":%lld}\n", (long long)n);
	return 0;
}

int main(int argc, char **argv)
{
	if (argc >= 4 && std::strcmp(argv[1], "writer") == 0)
		return writer(argv[2], std::atof(argv[3]));
	if (argc >= 4 && std::strcmp(argv[1], "client") == 0)
		return client(std::atoi(argv[2]), std::atof(argv[3]));
	json_tests();
	frame_tests();
	std::printf("%d passed, %d failed\n", g_pass, g_fail);
	return g_fail == 0 ? 0 : 1;
}
