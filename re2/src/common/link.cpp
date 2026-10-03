#include "link.hpp"
#include <chrono>
#include <cstring>

#ifdef _WIN32
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <winsock2.h>
#include <ws2tcpip.h>
using socklen_type = int;
static void close_native(intptr_t s) { closesocket(static_cast<SOCKET>(s)); }
static bool would_block() { return WSAGetLastError() == WSAEWOULDBLOCK; }
#else
#include <arpa/inet.h>
#include <cerrno>
#include <fcntl.h>
#include <netinet/in.h>
#include <netinet/tcp.h>
#include <poll.h>
#include <sys/socket.h>
#include <unistd.h>
static void close_native(intptr_t s) { ::close(static_cast<int>(s)); }
static bool would_block() { return errno == EAGAIN || errno == EWOULDBLOCK; }
#endif

namespace rcsk
{
	namespace
	{
		constexpr size_t kMaxQueued = 256;
		constexpr size_t kMaxInbox = 4096;

		bool wait_readable(intptr_t s, int timeout_ms)
		{
#ifdef _WIN32
			WSAPOLLFD p{static_cast<SOCKET>(s), POLLRDNORM, 0};
			return WSAPoll(&p, 1, timeout_ms) > 0;
#else
			pollfd p{static_cast<int>(s), POLLIN, 0};
			return ::poll(&p, 1, timeout_ms) > 0;
#endif
		}
	}

	Link::Link(uint16_t port) : port_(port)
	{
#ifdef _WIN32
		WSADATA wsa;
		WSAStartup(MAKEWORD(2, 2), &wsa);
#endif
	}

	Link::~Link()
	{
		stop();
#ifdef _WIN32
		WSACleanup();
#endif
	}

	void Link::start()
	{
		if (running_.exchange(true))
			return;
		thread_ = std::thread([this] { run(); });
	}

	void Link::stop()
	{
		if (!running_.exchange(false))
			return;
		wake_.notify_all();
		if (thread_.joinable())
			thread_.join();
		close_socket();
	}

	void Link::send(std::string json)
	{
		send_latest(std::move(json), 0);
	}

	void Link::send_latest(std::string json, int kind)
	{
		if (!connected_)
			return;
		std::lock_guard<std::mutex> g(lock_);
		if (kind != 0)
			for (auto &[k, line] : outbox_)
				if (k == kind)
				{
					line = std::move(json);
					wake_.notify_all();
					return;
				}
		while (outbox_.size() >= kMaxQueued)
			outbox_.pop_front();
		outbox_.emplace_back(kind, std::move(json));
		wake_.notify_all();
	}

	std::vector<std::string> Link::drain()
	{
		std::lock_guard<std::mutex> g(lock_);
		std::vector<std::string> out;
		out.swap(inbox_);
		return out;
	}

	void Link::close_socket()
	{
		if (sock_ != -1)
			close_native(sock_);
		sock_ = -1;
		connected_ = false;
	}

	bool Link::connect_once()
	{
		const intptr_t s = static_cast<intptr_t>(::socket(AF_INET, SOCK_STREAM, IPPROTO_TCP));
		if (s == -1)
			return false;
		sockaddr_in addr{};
		addr.sin_family = AF_INET;
		addr.sin_port = htons(port_);
		addr.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
		if (::connect(static_cast<decltype(::socket(0, 0, 0))>(s), reinterpret_cast<sockaddr *>(&addr), sizeof(addr)) != 0)
		{
			close_native(s);
			return false;
		}
		int one = 1;
		setsockopt(static_cast<decltype(::socket(0, 0, 0))>(s), IPPROTO_TCP, TCP_NODELAY, reinterpret_cast<const char *>(&one), sizeof(one));
#ifdef _WIN32
		u_long nb = 1;
		ioctlsocket(static_cast<SOCKET>(s), FIONBIO, &nb);
#else
		fcntl(static_cast<int>(s), F_SETFL, fcntl(static_cast<int>(s), F_GETFL) | O_NONBLOCK);
#endif
		sock_ = s;
		{
			std::lock_guard<std::mutex> g(lock_);
			outbox_.clear();
		}
		connected_ = true;
		++generation_;
		log("link: connected to the city on 127.0.0.1:" + std::to_string(port_));
		return true;
	}

	void Link::run()
	{
		std::string partial;
		bool dropping = false;
		std::string pending_write;
		size_t pending_off = 0;
		while (running_)
		{
			if (sock_ == -1)
			{
				if (!connect_once())
				{
					std::unique_lock<std::mutex> g(lock_);
					wake_.wait_for(g, std::chrono::seconds(1), [this] { return !running_; });
					continue;
				}
				partial.clear();
				pending_write.clear();
				pending_off = 0;
				dropping = false;
			}

			// Write whatever is queued (non-blocking; keep the remainder for the next pass).
			if (pending_write.empty())
			{
				std::lock_guard<std::mutex> g(lock_);
				while (!outbox_.empty() && pending_write.size() < 256 * 1024)
				{
					pending_write += outbox_.front().second;
					pending_write += '\n';
					outbox_.pop_front();
				}
				pending_off = 0;
			}
			bool failed = false;
			while (pending_off < pending_write.size())
			{
				const auto n = ::send(static_cast<decltype(::socket(0, 0, 0))>(sock_), pending_write.data() + pending_off,
					static_cast<int>(pending_write.size() - pending_off), 0);
				if (n > 0)
					pending_off += size_t(n);
				else
				{
					failed = !would_block();
					break;
				}
			}
			if (pending_off >= pending_write.size())
			{
				pending_write.clear();
				pending_off = 0;
			}

			// Read.
			if (!failed && wait_readable(sock_, pending_write.empty() ? 5 : 0))
			{
				char buf[65536];
				const auto n = ::recv(static_cast<decltype(::socket(0, 0, 0))>(sock_), buf, sizeof(buf), 0);
				if (n == 0 || (n < 0 && !would_block()))
					failed = true;
				else if (n > 0)
				{
					std::vector<std::string> lines;
					for (int k = 0; k < n; ++k)
					{
						if (buf[k] != '\n')
						{
							if (partial.size() < kMaxLine)
								partial += buf[k];
							else
								dropping = true;
							continue;
						}
						if (!dropping && !partial.empty())
							lines.push_back(std::move(partial));
						partial.clear();
						dropping = false;
					}
					if (!lines.empty())
					{
						std::lock_guard<std::mutex> g(lock_);
						for (auto &l : lines)
						{
							if (inbox_.size() >= kMaxInbox)
								inbox_.erase(inbox_.begin());
							inbox_.push_back(std::move(l));
						}
					}
				}
			}
			if (failed)
			{
				close_socket();
				log("link: the city went away; reconnecting");
				continue;
			}
			if (pending_write.empty())
			{
				std::unique_lock<std::mutex> g(lock_);
				wake_.wait_for(g, std::chrono::milliseconds(2), [this] { return !outbox_.empty() || !running_; });
			}
		}
	}
}
