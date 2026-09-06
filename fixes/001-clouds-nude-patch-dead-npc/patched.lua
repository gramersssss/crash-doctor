---------------------------------------------------------
-- Dynamic Anchor NPCs
-- MULTI-ANCHOR FINAL VERSION (4 COUPLES)
-- QUEST-GATED (q105_active == 1 AND q105_done == 0)
-- FIX: spatial late-entry sweep (3.0m outer radius)
-- FIX2: restore quest gate change detection + initial activation
---------------------------------------------------------

local DEBUG_MODE      = false
local DEBUG_POSITIONS = false

local Cron        = dofile("Cron.lua")
local GameSession = dofile("GameSession.lua")

---------------------------------------------------------
-- STATE
---------------------------------------------------------
local scriptActive = false
local tracked = {}
local pending = {}

---------------------------------------------------------
-- TIMING
---------------------------------------------------------
local ACTIVATION_DELAY = 1.0
local RETRY_INTERVAL   = 3.0
local VERIFY_DELAY     = 0.6

local SWEEP_INTERVAL = 1.5
local PENDING_TTL    = 12.0
local OUTER_MARGIN   = 3.0

local sweepTimer = 0

---------------------------------------------------------
-- DEBUG
---------------------------------------------------------
local function debug(msg)
    if DEBUG_MODE then
        print("[DynamicAnchorNPC] " .. msg)
    end
end

---------------------------------------------------------
-- QUEST GATE
---------------------------------------------------------
local function isQuestActive()
    local qs = Game.GetQuestsSystem()
    if not qs then return false end
    return (qs:GetFactStr("q105_active") or 0) == 1
       and (qs:GetFactStr("q105_done")   or 0) == 0
end

-- [fix] never call into an entity that may have been unloaded: re-resolve by ID, wrap engine calls in pcall
local function liveEntity(id)
    if not id then return nil end
    local ok, ent = pcall(function() return Game.FindEntityByID(id) end)
    if ok and ent then return ent end
    return nil
end

local function safePos(entity)
    if not entity then return nil end
    local ok, p = pcall(function() return entity:GetWorldPosition() end)
    return ok and p or nil
end

local function npcId(entity)
    if not entity then return "nil" end
    local ok, id = pcall(function() return entity:GetEntityID() end)
    return (ok and id) and tostring(id.hash) or "nil"
end

---------------------------------------------------------
-- ANCHORS
---------------------------------------------------------
local ANCHORS = {
    {
        name = "RightCouple",
        pos  = { x = -645.249, y = 806.538, z = 128.413 },
        radius = 0.75,
        rules = {
            { matchExact  = "prostitute_ma_prostitute_ma_003", target = "prostitute_ma_marv" },
            { matchPrefix = "citizen__corporat_wa_corporat_wa", target = "tiurina_mq055" }
        }
    },
    {
        name = "LeftCouple",
        pos  = { x = -627.064, y = 801.276, z = 128.522 },
        radius = 0.75,
        rules = {
            { matchPrefix = "service__sexworker_wa_doll_",      target = "fiona_mq055" },
            { matchPrefix = "citizen__corporat_ma_corporat_ma", target = "corporat_ma_05_n" }
        }
    },
    {
        name = "CornerCouple",
        pos  = { x = -630.999, y = 779.773, z = 128.873 },
        radius = 0.75,
        rules = {
            { matchPrefix = "service__sexworker_wa_doll_",      target = "lina_mq055" },
            { matchPrefix = "citizen__corporat_ma_corporat_ma", target = "corporat_ma_03_n" }
        }
    },
    {
        name = "BackroomCouple",
        pos  = { x = -650.077, y = 775.095, z = 128.918 },
        radius = 1.5,
        rules = {
            { matchPrefix = "morning_crowd_ma_",     target = "morning_crowd_ma_03_doll" },
            { matchPrefix = "king_of_the_stoop_ma_", target = "morning_crowd_ma_03_doll" },
            { matchExact  = "citizen__corporat_ma_corporat_ma_08", target = "corporat_ma_08_n" }
        }
    }
}

---------------------------------------------------------
-- HELPERS
---------------------------------------------------------
local function safeGetAppearance(entity)
    local ok, app = pcall(function()
        local a = entity:GetCurrentAppearanceName()
        return a and a.value or nil
    end)
    return ok and app or nil
end

local function distSq(p, a)
    local dx = p.x - a.pos.x
    local dy = p.y - a.pos.y
    local dz = p.z - a.pos.z
    return dx*dx + dy*dy + dz*dz
end

local function isAtAnchor(entity, anchor)
    local p = safePos(entity)
    if not p then return false end

    local d2 = distSq(p, anchor)
    local r2 = anchor.radius * anchor.radius

    if DEBUG_POSITIONS then
        debug(string.format("POS @%s id=%s d2=%.4f r2=%.4f", anchor.name, npcId(entity), d2, r2))
    end

    return d2 <= r2
end

local function isNearAnchor(entity, anchor)
    local p = safePos(entity)
    if not p then return false end
    local r = anchor.radius + OUTER_MARGIN
    return distSq(p, anchor) <= (r * r)
end

local function resolveRule(app, anchor)
    if not app then return nil end
    local a = app:lower()
    for _, rule in ipairs(anchor.rules) do
        if rule.matchExact and a == rule.matchExact then
            return rule
        end
        if rule.matchPrefix and a:find(rule.matchPrefix, 1, true) == 1 then
            return rule
        end
    end
    return nil
end

---------------------------------------------------------
-- RETRY (same as your original intent)
---------------------------------------------------------
local function scheduleRetry(entry)
    if not scriptActive or not entry or entry.resolved or entry.retryTimer or not entry.rule then return end

    entry.retryTimer = Cron.After(RETRY_INTERVAL, function()
        entry.retryTimer = nil
        if not scriptActive or not entry or not entry.entity or entry.resolved then return end
        local live = liveEntity(entry.id); if not live then entry.entity = nil; return end
        entry.entity = live
        pcall(function() entry.entity:ScheduleAppearanceChange(entry.rule.target) end)
        debug("Retry → @" .. entry.anchor.name .. " target=" .. entry.rule.target .. " id=" .. npcId(entry.entity))
    end)
end

---------------------------------------------------------
-- APPLY + VERIFY
---------------------------------------------------------
local function applyAndVerify(entry)
    if not scriptActive or not entry or not entry.entity then return end
    if entry.resolved or not entry.rule then return end

    local live0 = liveEntity(entry.id); if not live0 then entry.entity = nil; return end
    entry.entity = live0
    debug("Apply → @" .. entry.anchor.name .. " target=" .. entry.rule.target .. " id=" .. npcId(entry.entity))
    pcall(function() entry.entity:ScheduleAppearanceChange(entry.rule.target) end)

    Cron.After(VERIFY_DELAY, function()
        if not scriptActive or not entry or not entry.entity or entry.resolved then return end
        local live = liveEntity(entry.id); if not live then entry.entity = nil; return end
        entry.entity = live
        local cur = safeGetAppearance(entry.entity)
        if cur and cur:lower() == entry.rule.target:lower() then
            entry.resolved = true
            debug("Resolved → " .. entry.rule.target .. " @" .. entry.anchor.name .. " id=" .. npcId(entry.entity))
        else
            debug("Verify failed (" .. tostring(cur) .. ") @" .. entry.anchor.name .. " id=" .. npcId(entry.entity))
            scheduleRetry(entry)
        end
    end)
end

---------------------------------------------------------
-- START TRACKING
---------------------------------------------------------
local function startTrackingNPC(npc, anchor)
    local id = npc:GetEntityID()
    if not id or tracked[id.hash] then return end

    local entry = {
        entity   = npc,
        id       = id,
        anchor   = anchor,
        rule     = nil,
        resolved = false
    }

    tracked[id.hash] = entry
    debug("Track → @" .. anchor.name .. " id=" .. npcId(npc))

    entry.activationTimer = Cron.After(ACTIVATION_DELAY, function()
        if not scriptActive or not entry or not entry.entity or entry.resolved then return end
        local live = liveEntity(entry.id); if not live then entry.entity = nil; return end
        entry.entity = live

        local app = safeGetAppearance(entry.entity)
        debug("Resolve check → @" .. anchor.name .. " id=" .. npcId(entry.entity) .. " app=" .. tostring(app))

        local rule = resolveRule(app, anchor)
        if rule then
            entry.rule = rule
            applyAndVerify(entry)
        else
            debug("No rule yet (will wait for late-entry/retry context) → @" .. anchor.name ..
                  " id=" .. npcId(entry.entity) .. " app=" .. tostring(app))
            -- no rule: do nothing; if the NPC is wrong, it won’t be changed
        end
    end)
end

---------------------------------------------------------
-- UPDATE LOOP (quest gate + late-entry sweep)
---------------------------------------------------------
registerForEvent("onUpdate", function(delta)
    Cron.Update(delta)

    -- restore quest gate change detection (critical)
    local shouldBeActive = isQuestActive()
    if scriptActive ~= shouldBeActive then
        scriptActive = shouldBeActive
        debug("Quest gate changed → scriptActive=" .. tostring(scriptActive))

        if not scriptActive then
            for _, entry in pairs(tracked) do
                if entry.activationTimer then Cron.Halt(entry.activationTimer) end
                if entry.retryTimer then Cron.Halt(entry.retryTimer) end
            end
            tracked = {}
            pending = {}
        end
    end

    if not scriptActive then return end

    -- late-entry sweep (not per tick: interval gated)
    sweepTimer = sweepTimer + delta
    if sweepTimer < SWEEP_INTERVAL then return end
    sweepTimer = 0

    for hash, data in pairs(pending) do
        local npc    = liveEntity(data.id)
        local anchor = data.anchor

        if not npc then
            pending[hash] = nil

        elseif (os.clock() - data.since) > PENDING_TTL then
            debug("Pending timeout → @" .. anchor.name .. " id=" .. npcId(npc))
            pending[hash] = nil

        elseif isAtAnchor(npc, anchor) then
            debug("Late-entry → @" .. anchor.name .. " id=" .. npcId(npc))
            pending[hash] = nil
            startTrackingNPC(npc, anchor)
        end
    end
end)

---------------------------------------------------------
-- INIT & OBSERVERS
---------------------------------------------------------
registerForEvent("onInit", function()
    debug("Dynamic Anchor NPC initialized")

    GameSession.OnStart(function()
        scriptActive = isQuestActive()
        debug("GameSession start → scriptActive=" .. tostring(scriptActive))
    end)

    GameSession.OnEnd(function()
        scriptActive = false
        tracked = {}
        pending = {}
        debug("GameSession end → cleared state")
    end)

    -- IMPORTANT: hot-reload / already-loaded session support
    if GameSession.IsLoaded() then
        scriptActive = isQuestActive()
        debug("GameSession already loaded → scriptActive=" .. tostring(scriptActive))
    end

    ObserveAfter("NPCPuppet", "OnGameAttached", function(self)
        if not self then return end

        -- Always log attach when DEBUG is on, so you can see gating state.
        if DEBUG_MODE then
            debug("ATTACH (raw) app=" .. tostring(safeGetAppearance(self)) ..
                  " id=" .. npcId(self) ..
                  " scriptActive=" .. tostring(scriptActive))
        end

        if not scriptActive then return end

        local id = self:GetEntityID()
        if not id or tracked[id.hash] then return end

        for _, anchor in ipairs(ANCHORS) do
            if isAtAnchor(self, anchor) then
                startTrackingNPC(self, anchor)
                return
            end

            if isNearAnchor(self, anchor) then
                pending[id.hash] = {
                    npc    = self,
                    id     = id,
                    anchor = anchor,
                    since  = os.clock()
                }
                debug("Pending (near @" .. anchor.name .. ") id=" .. npcId(self))
                return
            end
        end
    end)

    ObserveAfter("NPCPuppet", "OnDetach", function(self)
        local okd, id = pcall(function() return self and self:GetEntityID() end)
        if not okd or not id then return end

        local entry = tracked[id.hash]
        if entry then
            if entry.activationTimer then Cron.Halt(entry.activationTimer) end
            if entry.retryTimer then Cron.Halt(entry.retryTimer) end
        end

        tracked[id.hash] = nil
        pending[id.hash] = nil
    end)
end)