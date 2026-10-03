-- Raccoon City Skylines: the gameplay half inside Resident Evil 2 (REFramework autorun script).
--
-- RaccoonSkylines.dll owns the link to Cities: Skylines; this script meets it through two files in
-- reframework/data/raccoon_skylines/ (REFramework's json functions can only reach reframework/data):
--   city.json  (written by the plugin)  the city's state: district, infection, power, daylight; city events
--   re2.json   (written here)           the survivor's position and health; RE2 events (kills, Mr. X, ...)
--
-- What it does with the city:
--   * the district's infection drives RE2's own dynamic difficulty (the game rank): a city losing to the T-virus
--     makes Raccoon City's zombies tougher;
--   * an on-screen line shows the district, its infection and whether the power is on.
--
-- RE2 type, field and method names below are marked VERIFY: they are this project's best knowledge of RE2 (2019)
-- and must be checked against your game build in REFramework's Object Explorer (Developer Tools) before you trust
-- them. Every lookup is guarded: a wrong name disables that one feature, logs once, and the rest keeps working.
-- The "Send test ..." buttons exercise the whole pipeline without any of them.

local CFG = {
    hud = true,
    rank = {
        enabled = true,
        singleton = "app.ropeway.GameRankSystem", -- VERIFY: RE2's dynamic difficulty manager
        field = "_GameRank",                      -- VERIFY: the current rank
        min = 0,
        max = 9,
        -- infection at or below this leaves the game's own rank alone
        threshold = 0.05,
    },
    player = {
        singleton = "app.ropeway.PlayerManager",  -- VERIFY
        get_player = "get_CurrentPlayer",          -- VERIFY: returns the player's via.GameObject
        hp_component = "app.ropeway.HitPointController", -- VERIFY
        hp_current = "get_CurrentHitPoint",        -- VERIFY
        hp_max = "get_DefaultHitPoint",            -- VERIFY
    },
    -- Methods to hook for events. Each is optional; fill in what your build's Object Explorer shows.
    hooks = {
        { kind = "kill",   type = "app.ropeway.EnemyController", method = "onDead" },        -- VERIFY
        { kind = "tyrant", type = "app.ropeway.enemy.em6200.Em6200Controller", method = "start" }, -- VERIFY (em6200 = Mr. X)
    },
    write_hz = 4,
}

local DATA = "raccoon_skylines/"

local state = {
    link = false,
    city = nil,
    seen_city_events = {},
    out_events = {},
    next_id = 0,
    next_io = 0,
    rank_applied = nil,
    warned = {},
    last_wave = nil,
}

do
    local ok, t = pcall(os.time)
    -- Event ids keep increasing across script reloads, so the plugin never mistakes new events for old ones.
    state.next_id = (ok and t or 0) * 1000
end

local function warn_once(key, msg)
    if state.warned[key] then return end
    state.warned[key] = true
    log.warn("[RaccoonSkylines] " .. msg)
end

local function push_event(kind, d)
    state.next_id = state.next_id + 1
    table.insert(state.out_events, { t = "ev", id = state.next_id, k = kind, d = d or {} })
    while #state.out_events > 32 do table.remove(state.out_events, 1) end
end

-- Event hooks -----------------------------------------------------------------------------------------------------

for _, h in ipairs(CFG.hooks) do
    local td = sdk.find_type_definition(h.type)
    local m = td and td:get_method(h.method)
    if m then
        local kind = h.kind
        sdk.hook(m, function(args) push_event(kind, {}) end, function(retval) return retval end)
        log.info("[RaccoonSkylines] hooked " .. h.type .. "." .. h.method .. " -> " .. kind)
    else
        warn_once("hook" .. h.kind, "no " .. h.type .. "." .. h.method .. " in this build; '" .. h.kind .. "' events are off (see CFG.hooks)")
    end
end

-- The survivor -----------------------------------------------------------------------------------------------------

local function player_state()
    local p = CFG.player
    local pm = sdk.get_managed_singleton(p.singleton)
    if not pm then return nil end
    local ok, go = pcall(function() return pm:call(p.get_player) end)
    if not ok or not go then return nil end
    local tr = go:call("get_Transform")
    if not tr then return nil end
    local pos = tr:call("get_Position")
    local hp = -1
    local okhp, val = pcall(function()
        local c = go:call("getComponent(System.Type)", sdk.typeof(p.hp_component))
        if not c then return -1 end
        local cur, max = c:call(p.hp_current), c:call(p.hp_max)
        if not cur or not max or max <= 0 then return -1 end
        return math.max(0, math.min(1, cur / max))
    end)
    if okhp then hp = val else warn_once("hp", "player health unavailable (see CFG.player)") end
    return { t = "player", p = { pos.x, pos.y, pos.z }, hp = hp, area = "" }
end

-- The game rank ----------------------------------------------------------------------------------------------------

local function apply_rank(inf)
    local r = CFG.rank
    if not r.enabled or inf == nil then return end
    if inf <= r.threshold then
        state.rank_applied = nil
        return
    end
    local sys = sdk.get_managed_singleton(r.singleton)
    if not sys then
        warn_once("rank", r.singleton .. " not found; the outbreak won't change the game rank (see CFG.rank)")
        return
    end
    local rank = math.floor(r.min + (r.max - r.min) * inf + 0.5)
    local ok, err = pcall(function() sys:set_field(r.field, rank) end)
    if ok then
        state.rank_applied = rank
    else
        warn_once("rankfield", "could not set " .. r.singleton .. "." .. r.field .. ": " .. tostring(err))
    end
end

-- The file bridge --------------------------------------------------------------------------------------------------

local function exchange()
    local c = json.load_file(DATA .. "city.json")
    if c then
        state.link = c.link == true
        if type(c.city) == "table" then state.city = c.city end
        if type(c.events) == "table" then
            for _, e in ipairs(c.events) do
                local id = e.id or 0
                if not state.seen_city_events[id] then
                    state.seen_city_events[id] = true
                    if e.k == "wave" then state.last_wave = os.clock() end
                end
            end
        end
    end
    if state.link and state.city then apply_rank(state.city.inf) end

    local out = { events = state.out_events }
    local ok, p = pcall(player_state)
    if ok and p then out.player = p elseif not ok then warn_once("player", "player position unavailable (see CFG.player)") end
    json.dump_file(DATA .. "re2.json", out)
end

re.on_frame(function()
    local now = os.clock()
    if now >= state.next_io then
        state.next_io = now + 1 / CFG.write_hz
        local ok, err = pcall(exchange)
        if not ok then warn_once("exchange", "exchange failed: " .. tostring(err)) end
    end

    if CFG.hud and state.link and state.city then
        local c = state.city
        local txt = string.format("%s  |  T-virus %d%%  |  %s", c.district or "?", math.floor((c.inf or 0) * 100 + 0.5),
            c.power == false and "BLACKOUT" or "power on")
        local col = c.power == false and 0xFF4040FF or ((c.inf or 0) > 0.5 and 0xFF40FF80 or 0xFFE0E0E0)
        draw.text(txt, 24, 24, col)
        if state.last_wave and now - state.last_wave < 4 then
            draw.text("OUTBREAK WAVE", 24, 44, 0xFF2020FF)
        end
    end
end)

re.on_draw_ui(function()
    if not imgui.tree_node("Raccoon City Skylines") then return end
    imgui.text(state.link and "Linked to Cities: Skylines" or "Waiting for Cities: Skylines (is RaccoonSkylines.dll in reframework/plugins?)")
    if state.city then
        local c = state.city
        imgui.text(string.format("District: %s   population %d", c.district or "?", c.pop or 0))
        imgui.text(string.format("Infection: district %.1f%%, city %.1f%%", (c.inf or 0) * 100, (c.cityInf or 0) * 100))
        imgui.text(string.format("Police %.0f%%  Health %.0f%%  Power %s  Daylight %.2f", (c.police or 0) * 100, (c.health or 0) * 100,
            tostring(c.power), c.daylight or 0))
    end
    imgui.text("Game rank set to: " .. (state.rank_applied and tostring(state.rank_applied) or "(the game's own)"))
    local changed
    changed, CFG.hud = imgui.checkbox("On-screen line", CFG.hud)
    changed, CFG.rank.enabled = imgui.checkbox("Infection drives the game rank", CFG.rank.enabled)
    if imgui.button("Send test kill") then push_event("kill", { enemy = "test" }) end
    imgui.same_line()
    if imgui.button("Send test Mr. X") then push_event("tyrant", {}) end
    imgui.same_line()
    if imgui.button("Send test G") then push_event("birkin", {}) end
    imgui.same_line()
    if imgui.button("Send safe room") then push_event("save", {}) end
    imgui.tree_pop()
end)

log.info("[RaccoonSkylines] script loaded")
