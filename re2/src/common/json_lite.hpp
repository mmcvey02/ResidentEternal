// A small JSON value, parser and writer for the link's messages (protocol/PROTOCOL.md). No dependencies.
#pragma once
#include <cstdint>
#include <optional>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

namespace rcsk::json
{
	struct Value
	{
		enum class Type { Null, Bool, Number, String, Array, Object };
		Type type = Type::Null;
		bool b = false;
		double num = 0.0;
		std::string str;
		std::vector<Value> arr;
		std::vector<std::pair<std::string, Value>> obj;

		bool is_object() const { return type == Type::Object; }
		bool is_array() const { return type == Type::Array; }
		bool is_number() const { return type == Type::Number; }
		bool is_string() const { return type == Type::String; }
		bool is_bool() const { return type == Type::Bool; }

		/// Member lookup; nullptr if this isn't an object or has no such key.
		const Value *get(std::string_view key) const;
		double number(std::string_view key, double fallback = 0.0) const;
		bool boolean(std::string_view key, bool fallback = false) const;
		std::string string(std::string_view key, std::string_view fallback = {}) const;
		/// Copies a numeric array member into out[0..n); false if it's missing, short or not numeric.
		bool numbers(std::string_view key, double *out, size_t n) const;
	};

	std::optional<Value> parse(std::string_view text);
	std::string dump(const Value &v);
	/// Appends a JSON string literal (quoted, escaped).
	void append_string(std::string &out, std::string_view s);
	/// Appends a number the way JSON wants it (NaN/inf become null).
	void append_number(std::string &out, double d);
}
