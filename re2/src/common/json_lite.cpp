#include "json_lite.hpp"
#include <cctype>
#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>

namespace rcsk::json
{
	namespace
	{
		constexpr int kMaxDepth = 64;

		struct Parser
		{
			std::string_view s;
			size_t i = 0;
			int depth = 0;

			void ws()
			{
				while (i < s.size() && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n'))
					++i;
			}

			bool lit(std::string_view w)
			{
				if (s.substr(i, w.size()) != w)
					return false;
				i += w.size();
				return true;
			}

			static void put_utf8(std::string &out, uint32_t cp)
			{
				if (cp < 0x80)
					out += char(cp);
				else if (cp < 0x800)
				{
					out += char(0xC0 | (cp >> 6));
					out += char(0x80 | (cp & 0x3F));
				}
				else if (cp < 0x10000)
				{
					out += char(0xE0 | (cp >> 12));
					out += char(0x80 | ((cp >> 6) & 0x3F));
					out += char(0x80 | (cp & 0x3F));
				}
				else
				{
					out += char(0xF0 | (cp >> 18));
					out += char(0x80 | ((cp >> 12) & 0x3F));
					out += char(0x80 | ((cp >> 6) & 0x3F));
					out += char(0x80 | (cp & 0x3F));
				}
			}

			bool hex4(uint32_t &cp)
			{
				if (i + 4 > s.size())
					return false;
				cp = 0;
				for (int k = 0; k < 4; ++k)
				{
					const char c = s[i++];
					cp <<= 4;
					if (c >= '0' && c <= '9')
						cp |= uint32_t(c - '0');
					else if (c >= 'a' && c <= 'f')
						cp |= uint32_t(c - 'a' + 10);
					else if (c >= 'A' && c <= 'F')
						cp |= uint32_t(c - 'A' + 10);
					else
						return false;
				}
				return true;
			}

			bool string(std::string &out)
			{
				if (i >= s.size() || s[i] != '"')
					return false;
				++i;
				while (i < s.size())
				{
					const char c = s[i++];
					if (c == '"')
						return true;
					if (c != '\\')
					{
						out += c;
						continue;
					}
					if (i >= s.size())
						return false;
					switch (s[i++])
					{
					case '"': out += '"'; break;
					case '\\': out += '\\'; break;
					case '/': out += '/'; break;
					case 'b': out += '\b'; break;
					case 'f': out += '\f'; break;
					case 'n': out += '\n'; break;
					case 'r': out += '\r'; break;
					case 't': out += '\t'; break;
					case 'u':
					{
						uint32_t cp;
						if (!hex4(cp))
							return false;
						if (cp >= 0xD800 && cp < 0xDC00 && s.substr(i, 2) == "\\u")
						{
							i += 2;
							uint32_t lo;
							if (!hex4(lo) || lo < 0xDC00 || lo >= 0xE000)
								return false;
							cp = 0x10000 + ((cp - 0xD800) << 10) + (lo - 0xDC00);
						}
						put_utf8(out, cp);
						break;
					}
					default:
						return false;
					}
				}
				return false;
			}

			bool number(double &out)
			{
				const size_t start = i;
				if (i < s.size() && s[i] == '-')
					++i;
				while (i < s.size() && (std::isdigit(static_cast<unsigned char>(s[i])) || s[i] == '.' || s[i] == 'e' || s[i] == 'E' || s[i] == '+' || s[i] == '-'))
					++i;
				if (i == start)
					return false;
				const std::string tmp(s.substr(start, i - start));
				char *end = nullptr;
				out = std::strtod(tmp.c_str(), &end);
				return end == tmp.c_str() + tmp.size();
			}

			bool value(Value &v)
			{
				if (++depth > kMaxDepth)
					return false;
				ws();
				if (i >= s.size())
					return false;
				bool ok = true;
				const char c = s[i];
				if (c == '{')
				{
					v.type = Value::Type::Object;
					++i;
					ws();
					if (i < s.size() && s[i] == '}')
						++i;
					else
						for (;;)
						{
							ws();
							std::string key;
							if (!string(key))
								return false;
							ws();
							if (i >= s.size() || s[i++] != ':')
								return false;
							Value member;
							if (!value(member))
								return false;
							v.obj.emplace_back(std::move(key), std::move(member));
							ws();
							if (i >= s.size())
								return false;
							if (s[i] == ',')
							{
								++i;
								continue;
							}
							if (s[i++] != '}')
								return false;
							break;
						}
				}
				else if (c == '[')
				{
					v.type = Value::Type::Array;
					++i;
					ws();
					if (i < s.size() && s[i] == ']')
						++i;
					else
						for (;;)
						{
							Value item;
							if (!value(item))
								return false;
							v.arr.push_back(std::move(item));
							ws();
							if (i >= s.size())
								return false;
							if (s[i] == ',')
							{
								++i;
								continue;
							}
							if (s[i++] != ']')
								return false;
							break;
						}
				}
				else if (c == '"')
				{
					v.type = Value::Type::String;
					ok = string(v.str);
				}
				else if (lit("true"))
				{
					v.type = Value::Type::Bool;
					v.b = true;
				}
				else if (lit("false"))
					v.type = Value::Type::Bool;
				else if (lit("null"))
					v.type = Value::Type::Null;
				else
				{
					v.type = Value::Type::Number;
					ok = number(v.num);
				}
				--depth;
				return ok;
			}
		};
	}

	const Value *Value::get(std::string_view key) const
	{
		if (type != Type::Object)
			return nullptr;
		for (const auto &[k, v] : obj)
			if (k == key)
				return &v;
		return nullptr;
	}

	double Value::number(std::string_view key, double fallback) const
	{
		const Value *v = get(key);
		return v && v->is_number() ? v->num : fallback;
	}

	bool Value::boolean(std::string_view key, bool fallback) const
	{
		const Value *v = get(key);
		return v && v->is_bool() ? v->b : fallback;
	}

	std::string Value::string(std::string_view key, std::string_view fallback) const
	{
		const Value *v = get(key);
		return v && v->is_string() ? v->str : std::string(fallback);
	}

	bool Value::numbers(std::string_view key, double *out, size_t n) const
	{
		const Value *v = get(key);
		if (!v || !v->is_array() || v->arr.size() < n)
			return false;
		for (size_t k = 0; k < n; ++k)
		{
			if (!v->arr[k].is_number())
				return false;
			out[k] = v->arr[k].num;
		}
		return true;
	}

	std::optional<Value> parse(std::string_view text)
	{
		Parser p{text};
		Value v;
		if (!p.value(v))
			return std::nullopt;
		p.ws();
		if (p.i != text.size())
			return std::nullopt;
		return v;
	}

	void append_string(std::string &out, std::string_view s)
	{
		out += '"';
		for (const char c : s)
		{
			switch (c)
			{
			case '"': out += "\\\""; break;
			case '\\': out += "\\\\"; break;
			case '\n': out += "\\n"; break;
			case '\r': out += "\\r"; break;
			case '\t': out += "\\t"; break;
			default:
				if (static_cast<unsigned char>(c) < 0x20)
				{
					char buf[8];
					std::snprintf(buf, sizeof(buf), "\\u%04x", c);
					out += buf;
				}
				else
					out += c;
			}
		}
		out += '"';
	}

	void append_number(std::string &out, double d)
	{
		if (!std::isfinite(d))
		{
			out += "null";
			return;
		}
		char buf[32];
		std::snprintf(buf, sizeof(buf), "%.9g", d);
		out += buf;
	}

	static void dump_into(std::string &out, const Value &v)
	{
		switch (v.type)
		{
		case Value::Type::Null: out += "null"; break;
		case Value::Type::Bool: out += v.b ? "true" : "false"; break;
		case Value::Type::Number: append_number(out, v.num); break;
		case Value::Type::String: append_string(out, v.str); break;
		case Value::Type::Array:
			out += '[';
			for (size_t k = 0; k < v.arr.size(); ++k)
			{
				if (k)
					out += ',';
				dump_into(out, v.arr[k]);
			}
			out += ']';
			break;
		case Value::Type::Object:
			out += '{';
			for (size_t k = 0; k < v.obj.size(); ++k)
			{
				if (k)
					out += ',';
				append_string(out, v.obj[k].first);
				out += ':';
				dump_into(out, v.obj[k].second);
			}
			out += '}';
			break;
		}
	}

	std::string dump(const Value &v)
	{
		std::string out;
		dump_into(out, v);
		return out;
	}
}
