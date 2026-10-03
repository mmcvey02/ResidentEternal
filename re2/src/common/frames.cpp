#include "frames.hpp"
#include <atomic>
#include <chrono>
#include <cstring>

#ifdef _WIN32
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#else
#include <fcntl.h>
#include <sys/mman.h>
#include <sys/stat.h>
#include <unistd.h>
#endif

namespace rcsk
{
	namespace
	{
		template <typename T>
		T rd(const uint8_t *p)
		{
			T v;
			std::memcpy(&v, p, sizeof(T));
			return v;
		}

		template <typename T>
		void wr(uint8_t *p, T v)
		{
			std::memcpy(p, &v, sizeof(T));
		}

		uint64_t now_ms()
		{
			using namespace std::chrono;
			return uint64_t(duration_cast<milliseconds>(steady_clock::now().time_since_epoch()).count());
		}

		uint32_t current_pid()
		{
#ifdef _WIN32
			return GetCurrentProcessId();
#else
			return uint32_t(getpid());
#endif
		}
	}

	Mapping::~Mapping()
	{
		close();
	}

#ifdef _WIN32
	static std::wstring mapping_name(const std::string &name)
	{
		std::wstring w = L"Local\\RaccoonSkylines.";
		for (char c : name)
			w += wchar_t(c);
		return w;
	}

	bool Mapping::create(const std::string &name, size_t size)
	{
		close();
		HANDLE h = CreateFileMappingW(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE, DWORD(uint64_t(size) >> 32), DWORD(size), mapping_name(name).c_str());
		if (!h)
			return false;
		void *v = MapViewOfFile(h, FILE_MAP_ALL_ACCESS, 0, 0, size);
		if (!v)
		{
			CloseHandle(h);
			return false;
		}
		handle_ = h;
		data_ = static_cast<uint8_t *>(v);
		size_ = size;
		return true;
	}

	bool Mapping::open_existing(const std::string &name)
	{
		close();
		HANDLE h = OpenFileMappingW(FILE_MAP_ALL_ACCESS, FALSE, mapping_name(name).c_str());
		if (!h)
			return false;
		const auto *head = static_cast<const uint8_t *>(MapViewOfFile(h, FILE_MAP_READ, 0, 0, kFrameHeader));
		if (!head || rd<uint32_t>(head) != kFrameMagic)
		{
			if (head)
				UnmapViewOfFile(head);
			CloseHandle(h);
			return false;
		}
		const size_t size = rd<uint32_t>(head + 8) + size_t(rd<int64_t>(head + 16)) * rd<uint32_t>(head + 12);
		UnmapViewOfFile(head);
		void *v = MapViewOfFile(h, FILE_MAP_ALL_ACCESS, 0, 0, size);
		if (!v)
		{
			CloseHandle(h);
			return false;
		}
		handle_ = h;
		data_ = static_cast<uint8_t *>(v);
		size_ = size;
		return true;
	}

	void Mapping::close()
	{
		if (data_)
			UnmapViewOfFile(data_);
		if (handle_)
			CloseHandle(static_cast<HANDLE>(handle_));
		data_ = nullptr;
		handle_ = nullptr;
		size_ = 0;
	}
#else
	static std::string mapping_path(const std::string &name)
	{
		return "/dev/shm/RaccoonSkylines." + name;
	}

	bool Mapping::create(const std::string &name, size_t size)
	{
		close();
		const int fd = ::open(mapping_path(name).c_str(), O_RDWR | O_CREAT, 0600);
		if (fd < 0)
			return false;
		if (ftruncate(fd, off_t(size)) != 0)
		{
			::close(fd);
			return false;
		}
		void *v = mmap(nullptr, size, PROT_READ | PROT_WRITE, MAP_SHARED, fd, 0);
		::close(fd);
		if (v == MAP_FAILED)
			return false;
		data_ = static_cast<uint8_t *>(v);
		size_ = size;
		return true;
	}

	bool Mapping::open_existing(const std::string &name)
	{
		close();
		const int fd = ::open(mapping_path(name).c_str(), O_RDWR);
		if (fd < 0)
			return false;
		struct stat st{};
		if (fstat(fd, &st) != 0 || size_t(st.st_size) < kFrameHeader)
		{
			::close(fd);
			return false;
		}
		void *v = mmap(nullptr, size_t(st.st_size), PROT_READ | PROT_WRITE, MAP_SHARED, fd, 0);
		::close(fd);
		if (v == MAP_FAILED)
			return false;
		data_ = static_cast<uint8_t *>(v);
		size_ = size_t(st.st_size);
		if (rd<uint32_t>(data_) != kFrameMagic)
		{
			close();
			return false;
		}
		return true;
	}

	void Mapping::close()
	{
		if (data_)
			munmap(data_, size_);
		data_ = nullptr;
		size_ = 0;
	}
#endif

	bool FrameWriter::open(const std::string &name, uint32_t max_w, uint32_t max_h, uint32_t slots)
	{
		max_w_ = max_w;
		max_h_ = max_h;
		slots_ = slots;
		stride_ = int64_t(max_w) * max_h * 4;
		seq_.assign(slots, 0);
		publish_ = 0;
		next_ = 0;
		if (!map_.create(name, kFrameHeader + size_t(stride_) * slots))
			return false;
		uint8_t *m = map_.data();
		std::memset(m, 0, kFrameHeader);
		wr<uint32_t>(m + 4, kFrameVersion);
		wr<uint32_t>(m + 8, kFrameHeader);
		wr<uint32_t>(m + 12, slots);
		wr<int64_t>(m + 16, stride_);
		wr<uint32_t>(m + 24, max_w);
		wr<uint32_t>(m + 28, max_h);
		wr<int64_t>(m + 32, 0);
		wr<int32_t>(m + 40, -1);
		wr<uint32_t>(m + 44, current_pid());
		std::atomic_thread_fence(std::memory_order_release);
		wr<uint32_t>(m, kFrameMagic); // last: readers check it
		return true;
	}

	bool FrameWriter::write(const FrameInfo &info, const uint8_t *rgba)
	{
		if (!is_open() || info.width == 0 || info.height == 0 || info.width > max_w_ || info.height > max_h_)
			return false;
		uint8_t *m = map_.data();
		const uint32_t i = next_;
		next_ = (next_ + 1) % slots_;
		uint8_t *d = m + kSlotDesc + kSlotDescBytes * i;
		wr<int64_t>(d, ++seq_[i]);
		std::atomic_thread_fence(std::memory_order_release);
		std::memcpy(m + kFrameHeader + stride_ * i, rgba, size_t(info.width) * info.height * 4);
		wr<int64_t>(d + 8, info.frame);
		wr<int64_t>(d + 16, info.host_frame);
		wr<uint32_t>(d + 24, info.width);
		wr<uint32_t>(d + 28, info.height);
		wr<float>(d + 32, info.near_plane);
		wr<float>(d + 36, info.far_plane);
		wr<float>(d + 40, info.fov);
		wr<uint32_t>(d + 44, info.flags & ~kFlagDepth);
		wr<float>(d + 48, info.daylight);
		wr<float>(d + 52, info.infection);
		std::atomic_thread_fence(std::memory_order_release);
		wr<int64_t>(d, ++seq_[i]);
		++publish_;
		wr<int32_t>(m + 40, int32_t(i));
		std::atomic_thread_fence(std::memory_order_release);
		wr<int64_t>(m + 32, publish_);
		return true;
	}

	bool FrameReader::ensure(const std::string &name)
	{
		if (is_open())
			return true;
		const uint64_t now = now_ms();
		if (now < next_attempt_ms_)
			return false;
		next_attempt_ms_ = now + 1000;
		return map_.open_existing(name);
	}

	bool FrameReader::latest(FrameInfo &info, std::vector<uint8_t> &rgba)
	{
		if (!is_open())
			return false;
		const uint8_t *m = map_.data();
		const int64_t publish = rd<int64_t>(m + 32);
		std::atomic_thread_fence(std::memory_order_acquire);
		const int32_t slot = rd<int32_t>(m + 40);
		const uint32_t slots = rd<uint32_t>(m + 12);
		const int64_t stride = rd<int64_t>(m + 16);
		if (publish == last_publish_ || slot < 0 || uint32_t(slot) >= slots)
			return false;
		const uint8_t *d = m + kSlotDesc + kSlotDescBytes * slot;
		const int64_t seq = rd<int64_t>(d);
		if (seq & 1)
			return false;
		std::atomic_thread_fence(std::memory_order_acquire);
		FrameInfo f;
		f.frame = rd<int64_t>(d + 8);
		f.host_frame = rd<int64_t>(d + 16);
		f.width = rd<uint32_t>(d + 24);
		f.height = rd<uint32_t>(d + 28);
		f.near_plane = rd<float>(d + 32);
		f.far_plane = rd<float>(d + 36);
		f.fov = rd<float>(d + 40);
		f.flags = rd<uint32_t>(d + 44);
		f.daylight = rd<float>(d + 48);
		f.infection = rd<float>(d + 52);
		const size_t bytes = size_t(f.width) * f.height * 4;
		if (f.width == 0 || f.height == 0 || int64_t(bytes) > stride || kFrameHeader + size_t(stride) * (slot + 1) > map_.size())
			return false;
		rgba.resize(bytes);
		std::memcpy(rgba.data(), m + kFrameHeader + stride * slot, bytes);
		std::atomic_thread_fence(std::memory_order_acquire);
		if (rd<int64_t>(d) != seq)
			return false; // lapped by the writer mid-copy
		last_publish_ = publish;
		info = f;
		return true;
	}
}
