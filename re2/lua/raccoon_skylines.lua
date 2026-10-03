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
-- RE2 names below marked VERIFY are this project's best knowledge of RE2 (2019) and should be checked against your
-- build in REFramework's Object Explorer (Developer Tools). Every lookup is guarded: a wrong name disables that one feature, logs once, and the rest keeps working.
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
    -- Events come from watching the scene twice a second (no method hooks needed). These type names are confirmed
    -- in RE2's type list (REFramework log, March 2025 build); the hit point method names are still VERIFY.
    watch = {
        enemy = "app.ropeway.EnemyController",
        enemy_hp = { "app.ropeway.EnemyHitPointController", "app.ropeway.HitPointController" },
        hp_current = "get_CurrentHitPoint", -- VERIFY
        tyrant = "app.ropeway.enemy.em6200.Em6200ChaserController", -- Mr. X (em6200)
        hz = 2,
    },
    -- Optional extra method hooks, if you find better ones in the Object Explorer: { kind = "kill", type = "...", method = "..." }
    hooks = {},
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

-- Watching the scene for kills and Mr. X ---------------------------------------------------------------------------

local watch_state = { alive = {}, tyrant = false, next = 0 }

local function scene_components(type_name)
    local sm = sdk.get_native_singleton("via.SceneManager")
    local smt = sdk.find_type_definition("via.SceneManager")
    if not sm or not smt then return nil end
    local scene = sdk.call_native_func(sm, smt, "get_CurrentScene")
    if not scene then return nil end
    local arr = scene:call("findComponents(System.Type)", sdk.typeof(type_name))
    if not arr then return nil end
    -- REFramework's SystemArray: get_elements(), or get_size() + get_element(i).
    local ok, elems = pcall(function() return arr:get_elements() end)
    if ok and type(elems) == "table" then return elems end
    local out = {}
    for i = 0, (arr:get_size() or 0) - 1 do
        local c = arr:get_element(i)
        if c then table.insert(out, c) end
    end
    return out
end

local function enemy_hp(enemy)
    local go = enemy:call("get_GameObject")
    if not go then return nil end
    for _, t in ipairs(CFG.watch.enemy_hp) do
        local td = sdk.typeof(t)
        if td then
            local c = go:call("getComponent(System.Type)", td)
            if c then return c:call(CFG.watch.hp_current) end
        end
    end
    return nil
end

local function watch_scene()
    local w = CFG.watch
    local enemies = scene_components(w.enemy)
    if enemies then
        local seen = {}
        for _, e in ipairs(enemies) do
            local key = e:get_address()
            seen[key] = true
            local ok, hp = pcall(enemy_hp, e)
            if ok and hp ~= nil then
                local alive = hp > 0
                if watch_state.alive[key] == true and not alive then
                    push_event("kill", { enemy = e:get_type_definition():get_full_name() })
                end
                watch_state.alive[key] = alive
            elseif not ok then
                warn_once("enemyhp", "enemy hit points unreadable (" .. tostring(hp) .. "); kill events are off (see CFG.watch)")
            end
        end
        for key in pairs(watch_state.alive) do
            if not seen[key] then watch_state.alive[key] = nil end -- despawned or unloaded: not a kill
        end
    end
    local tyrants = scene_components(w.tyrant)
    local present = tyrants ~= nil and #tyrants > 0
    if present and not watch_state.tyrant then push_event("tyrant", {}) end
    watch_state.tyrant = present
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
    if now >= watch_state.next then
        watch_state.next = now + 1 / CFG.watch.hz
        local ok, err = pcall(watch_scene)
        if not ok then warn_once("watch", "watching the scene failed: " .. tostring(err)) end
    end
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
