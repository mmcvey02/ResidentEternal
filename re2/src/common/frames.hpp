// Frames in named shared memory, layout in protocol/PROTOCOL.md section 2. Windows: "Local\RaccoonSkylines.<name>";
// elsewhere (tests): /dev/shm/RaccoonSkylines.<name>.
#pragma once
#include <cstdint>
#include <string>
#include <vector>

namespace rcsk
{
	constexpr uint32_t kFrameMagic = 0x4B534352; // "RCSK"
	constexpr uint32_t kFrameVersion = 1;
	constexpr uint32_t kFrameHeader = 4096;
	constexpr uint32_t kSlotDesc = 256;
	constexpr uint32_t kSlotDescBytes = 128;
	constexpr uint32_t kFlagDepth = 1;
	constexpr uint32_t kFlagBottomUp = 2;

	struct FrameInfo
	{
		int64_t frame = 0, host_frame = 0;
		uint32_t width = 0, height = 0;
		float near_plane = 0.1f, far_plane = 10000.0f, fov = 60.0f;
		uint32_t flags = 0;
		float daylight = 1.0f, infection = 0.0f;
	};

	class Mapping
	{
	public:
		~Mapping();
		bool create(const std::string &name, size_t size);
		bool open_existing(const std::string &name);
		void close();
		uint8_t *data() const { return data_; }
		size_t size() const { return size_; }

	private:
		uint8_t *data_ = nullptr;
		size_t size_ = 0;
		void *handle_ = nullptr;
	};

	class FrameWriter
	{
	public:
		bool open(const std::string &name, uint32_t max_w, uint32_t max_h, uint32_t slots = 3);
		bool is_open() const { return map_.data() != nullptr; }
		/// rgba: width * height * 4 bytes. False if the frame is larger than the mapping allows.
		bool write(const FrameInfo &info, const uint8_t *rgba);
		uint32_t max_width() const { return max_w_; }
		uint32_t max_height() const { return max_h_; }

	private:
		Mapping map_;
		uint32_t max_w_ = 0, max_h_ = 0, slots_ = 0, next_ = 0;
		int64_t stride_ = 0, publish_ = 0;
		std::vector<int64_t> seq_;
	};

	class FrameReader
	{
	public:
		/// Tries to open the mapping (cheap to call every frame: retries at most once a second).
		bool ensure(const std::string &name);
		bool is_open() const { return map_.data() != nullptr; }
		/// Copies the newest unseen frame. False if there is none or it was overwritten mid-copy.
		bool latest(FrameInfo &info, std::vector<uint8_t> &rgba);
		void close() { map_.close(); }

	private:
		Mapping map_;
		int64_t last_publish_ = -1;
		uint64_t next_attempt_ms_ = 0;
	};
}
