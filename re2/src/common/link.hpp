// The RE2 end of the link: a TCP client to the city on 127.0.0.1 that reconnects by itself and exchanges
// newline-delimited JSON (protocol/PROTOCOL.md section 1). Portable (Winsock / BSD sockets) so it is tested on Linux.
#pragma once
#include <atomic>
#include <condition_variable>
#include <cstdint>
#include <deque>
#include <functional>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

namespace rcsk
{
	class Link
	{
	public:
		static constexpr size_t kMaxLine = 64 * 1024;

		explicit Link(uint16_t port);
		~Link();
		Link(const Link &) = delete;
		Link &operator=(const Link &) = delete;

		void start();
		void stop();

		/// Queues one message (a JSON object without the newline). Dropped while disconnected.
		void send(std::string json);
		/// Replaces any queued message of the same kind (for state that only needs its newest value, like "cam").
		void send_latest(std::string json, int kind);
		/// Every complete line received since the last call.
		std::vector<std::string> drain();

		bool connected() const { return connected_; }
		/// Bumped on every new connection, so the caller knows to say hello again.
		uint32_t generation() const { return generation_; }

		std::function<void(const std::string &)> log = [](const std::string &) {};

	private:
		void run();
		bool connect_once();
		void close_socket();

		uint16_t port_;
		std::thread thread_;
		std::atomic<bool> running_{false};
		std::atomic<bool> connected_{false};
		std::atomic<uint32_t> generation_{0};
		std::mutex lock_;
		std::condition_variable wake_;
		std::deque<std::pair<int, std::string>> outbox_;
		std::vector<std::string> inbox_;
		intptr_t sock_ = -1;
	};
}
