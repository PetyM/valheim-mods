using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace LevelEasily;

[BepInPlugin(Guid, "LevelEasily", "0.2.0")]
[BepInDependency(Jotunn.Main.ModGuid)]
public sealed class Plugin : BaseUnityPlugin
{
    private const string Guid = "michal.leveleasily";
    private const string PieceName = "LevelEasily_LevelArea";
    private const float FullColourDeviation = 1f;
    private const float AimDistance = 50f;
    private const float MeasureInterval = 0.5f;
    private const float TargetTolerance = 0.02f;
    private const float ClaimWait = 0.5f;
    private const float PlayerClearance = 1.5f;
    private const float PlayerSafeRise = 0.5f;
    private static readonly Color Level = new(0.2f, 0.95f, 0.3f, 0.9f);
    private static readonly Color TooHigh = new(1f, 0.1f, 0.1f, 0.9f);
    private static readonly Color TooLow = new(0.1f, 0.35f, 1f, 0.9f);
    private static readonly Color OutOfReach = new(0.75f, 0.2f, 1f, 0.9f);
    private static readonly Color UnderPiece = new(0.55f, 0.55f, 0.55f, 0.9f);
    private static readonly Color NoPickaxe = new(0.4f, 0f, 0f, 0.9f);
    private static readonly Color Unaffordable = new(0.15f, 0.15f, 0.15f, 0.9f);

    private static Plugin instance;
    private ConfigEntry<KeyboardShortcut> lockKey, raiseKey, lowerKey, widerKey, narrowerKey, longerKey, shorterKey;
    private ConfigEntry<float> step, pickaxeSeconds, staminaDiscount;
    private ConfigEntry<int> maxSize;
    private ConfigEntry<bool> preserveSoil;
    private int aimMask, pieceMask;
    private VanillaRates rates;
    private GaugeMesh preview;
    private AreaPlan plan;
    private Budget budget;
    private Cost cost;
    private Shortage shortage;
    private float lastMeasure;
    private bool working;
    private Vector2Int size = new(4, 4);
    private float? lockedHeight;
    private Vector2Int? gridAnchor;
    private string readout;
    private GUIStyle readoutStyle;

    private void Awake()
    {
        instance = this;
        lockKey = Config.Bind("Keys", "Lock", new KeyboardShortcut(KeyCode.L),
            "Locks the target height (top surface of the building piece under the cursor, otherwise the ground under the player) and the area grid. Press again to unlock.");
        raiseKey = Config.Bind("Keys", "Raise target", new KeyboardShortcut(KeyCode.PageUp));
        lowerKey = Config.Bind("Keys", "Lower target", new KeyboardShortcut(KeyCode.PageDown));
        widerKey = Config.Bind("Keys", "Wider", new KeyboardShortcut(KeyCode.RightArrow));
        narrowerKey = Config.Bind("Keys", "Narrower", new KeyboardShortcut(KeyCode.LeftArrow));
        longerKey = Config.Bind("Keys", "Longer", new KeyboardShortcut(KeyCode.UpArrow));
        shorterKey = Config.Bind("Keys", "Shorter", new KeyboardShortcut(KeyCode.DownArrow));
        step = Config.Bind("Keys", "Height step", 0.1f, "How many metres one key press moves the target.");
        maxSize = Config.Bind("Area", "Largest side", 16,
            new ConfigDescription("Largest length of an area side in metres.", new AcceptableValueRange<int>(1, 32)));
        preserveSoil = Config.Bind("Area", "Preserve cultivated and paved ground", true,
            "Levels cultivated and paved ground but does not paint it over with dirt.");
        pickaxeSeconds = Config.Bind("Cost", "Pickaxe hit time", 1f,
            new ConfigDescription("How many seconds one counted pickaxe hit blocks the hoe for.", new AcceptableValueRange<float>(0f, 5f)));
        staminaDiscount = Config.Bind("Cost", "Stamina discount", 0.3f,
            new ConfigDescription("How much less stamina hoe swings and pickaxe hits cost compared with manual work (0.3 = 30 % less).",
                new AcceptableValueRange<float>(0f, 1f)));
        aimMask = LayerMask.GetMask("piece", "piece_nonsolid", "Default", "static_solid", "terrain");
        pieceMask = LayerMask.GetMask("piece", "piece_nonsolid", "Default", "static_solid");
        PrefabManager.OnVanillaPrefabsAvailable += AddLevelAreaPiece;
        new Harmony(Guid).PatchAll();
    }

    private void AddLevelAreaPiece()
    {
        PrefabManager.OnVanillaPrefabsAvailable -= AddLevelAreaPiece;
        rates = VanillaRates.FromHoe(PrefabManager.Cache.GetPrefab<ItemDrop>("Hoe").m_itemData.m_shared.m_buildPieces, Logger);
        var prefab = PrefabManager.Instance.CreateClonedPrefab(PieceName, rates.LevelPiece.gameObject);
        foreach (var projector in prefab.GetComponentsInChildren<CircleProjector>(true)) DestroyImmediate(projector);
        foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
        PieceManager.Instance.AddPiece(new CustomPiece(prefab, fixReference: false, new PieceConfig
        {
            Name = "Level area",
            Description = "Levels a rectangle snapped to the metre grid to the target height with one click. " +
                          "Costs as much as the manual work would: hoe swings, pickaxe hits from your inventory and stone for filling.\n" +
                          "Arrows: size · L: lock height and grid · PageUp/PageDown: move target",
            PieceTable = "Hoe",
        }));
    }

    private static bool IsAreaPiece(Piece piece) => piece && piece.name == PieceName;

    private void Update()
    {
        var player = Player.m_localPlayer;
        var ghost = player ? player.m_placementGhost : null;
        if (!ghost || !ghost.activeSelf || !IsAreaPiece(player.GetSelectedPiece()) || player.GetRightItem() == null)
        {
            plan = null;
            preview?.SetVisible(false);
            return;
        }
        if (AcceptsInput()) HandleKeys(player);

        budget = Budget.Of(player, rates, pickaxeSeconds.Value, staminaDiscount.Value);
        var area = SnapArea(ghost.transform.position);
        float ground = GroundUnder(player), target = lockedHeight ?? ground;
        bool freeFill = target <= ground + AreaPlan.ChangeThreshold, hasPickaxe = budget.Pickaxe != null;
        if (plan == null || plan.Area != area || Mathf.Abs(plan.Target - target) > TargetTolerance || plan.FreeFill != freeFill ||
            Time.time - lastMeasure > MeasureInterval)
        {
            plan = AreaPlan.Measure(area, target, freeFill, hasPickaxe, ghost, pieceMask);
            lastMeasure = Time.time;
        }
        (cost, shortage) = budget.Select(plan, player.transform.position, rates);
        readout = Readout();
        preview ??= new GaugeMesh();
        preview.SetVisible(true);
        preview.Rebuild(plan.Vertices.Select(v => (v.Point, ColourFor(v))));
    }

    private void OnGUI()
    {
        if (plan == null) return;
        readoutStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, fontSize = 18 };
        GUI.Label(new Rect(Screen.width / 2f - 450, Screen.height / 2f + 40, 900, 160), readout, readoutStyle);
    }

    private static bool AcceptsInput() =>
        !Console.IsVisible() && !(Chat.instance && Chat.instance.HasFocus()) && !TextInput.IsVisible() &&
        !Menu.IsVisible() && !InventoryGui.IsVisible() && !Hud.IsPieceSelectionVisible();

    private void HandleKeys(Player player)
    {
        var forward = GameCamera.instance.transform.forward;
        int across = Steps(widerKey, narrowerKey), along = Steps(longerKey, shorterKey);
        size += Mathf.Abs(forward.z) >= Mathf.Abs(forward.x) ? new Vector2Int(across, along) : new Vector2Int(along, across);
        size.Clamp(Vector2Int.one, Vector2Int.one * maxSize.Value);

        if (lockKey.Value.IsDown())
        {
            lockedHeight = lockedHeight.HasValue ? null : AimedHeight(player);
            gridAnchor = lockedHeight.HasValue ? plan?.Area.min : null;
        }
        int vertical = Steps(raiseKey, lowerKey);
        if (vertical == 0) return;
        lockedHeight = (lockedHeight ?? GroundUnder(player)) + vertical * step.Value;
        gridAnchor ??= plan?.Area.min;
    }

    private static int Steps(ConfigEntry<KeyboardShortcut> up, ConfigEntry<KeyboardShortcut> down) =>
        (up.Value.IsDown() ? 1 : 0) - (down.Value.IsDown() ? 1 : 0);

    /// <summary>Like vanilla Level ground: the ground under the player, not the feet height (jumping, swimming, floors).</summary>
    private static float GroundUnder(Player player) => ZoneSystem.instance.GetGroundHeight(player.transform.position);

    private float AimedHeight(Player player)
    {
        var camera = GameCamera.instance.transform;
        if (!Physics.Raycast(camera.position, camera.forward, out var hit, AimDistance, aimMask) ||
            !hit.collider.GetComponentInParent<Piece>())
            return GroundUnder(player);
        // Top surface of the hit piece at the hit point, even when the cursor aims at its side.
        var bounds = hit.collider.bounds;
        var down = new Ray(new Vector3(hit.point.x, bounds.max.y + 1, hit.point.z), Vector3.down);
        return hit.collider.Raycast(down, out var top, bounds.size.y + 2) ? top.point.y : hit.point.y;
    }

    private RectInt SnapArea(Vector3 cursor)
    {
        var min = new Vector2Int(Mathf.RoundToInt(cursor.x - size.x / 2f), Mathf.RoundToInt(cursor.z - size.y / 2f));
        if (gridAnchor is { } anchor)
            min = anchor + Vector2Int.RoundToInt(new Vector2((min.x - anchor.x) / (float)size.x, (min.y - anchor.y) / (float)size.y)) * size;
        return new RectInt(min, size);
    }

    private static Color ColourFor(VertexPlan v)
    {
        switch (v.State)
        {
            case VertexState.UnderPiece: return UnderPiece;
            case VertexState.NeedsPickaxe: return NoPickaxe;
            case VertexState.Level: return v.Unreachable ? OutOfReach : Level;
        }
        var colour = v.Unreachable
            ? OutOfReach
            : Color.Lerp(Color.white, v.Rise < 0 ? TooHigh : TooLow,
                Mathf.Lerp(0.4f, 1f, (Mathf.Abs(v.Rise) - AreaPlan.ChangeThreshold) / FullColourDeviation));
        return v.Selected ? colour : Color.Lerp(colour, Unaffordable, 0.7f);
    }

    private static string ShortageText(Shortage s) => s switch
    {
        Shortage.Stamina => "not enough stamina",
        Shortage.HoeWear => "the hoe would break",
        Shortage.PickaxeWear => "the pickaxe would break",
        Shortage.Stone => "not enough stone",
        _ => "",
    };

    private string Readout()
    {
        var text = new StringBuilder();
        text.Append($"{size.x} × {size.y} m · target {plan.Target:0.00} m");
        if (lockedHeight.HasValue) text.Append(" · locked");
        if (plan.Target < ZoneSystem.instance.m_waterLevel) text.Append(" · below sea level");
        text.AppendLine();

        int wanted = plan.Vertices.Count(v => v.Wanted);
        if (working) text.AppendLine("levelling…");
        else if (wanted == 0) text.AppendLine("nothing to level");
        else
        {
            text.Append($"hoe {cost.AllSwings}× · pickaxe {cost.PickHits}× · stone {cost.Stone} · stamina {cost.Stamina:0} · {cost.Seconds:0.#} s");
            if (cost.Returned > 0)
                text.Append($" · you dig up {cost.Returned}× {Localization.instance.Localize(budget.Yield.Item.m_itemData.m_shared.m_name)}");
            text.AppendLine();
            text.Append($"wear: hoe {cost.HoeWear:0.#}");
            if (cost.PickHits > 0) text.Append($", {Localization.instance.Localize(budget.Pickaxe.m_shared.m_name)} {cost.PickWear:0.#}");
            if (cost.Vertices < wanted) text.Append($" · levels {cost.Vertices} of {wanted} points ({ShortageText(shortage)})");
            text.AppendLine();
        }

        var skipped = new List<string>();
        int underPiece = plan.Count(VertexState.UnderPiece), noPickaxe = plan.Count(VertexState.NeedsPickaxe);
        int outOfReach = plan.Vertices.Count(v => v.Unreachable);
        if (underPiece > 0) skipped.Add($"under building {underPiece}");
        if (noPickaxe > 0) skipped.Add($"no pickaxe {noPickaxe}");
        if (outOfReach > 0) skipped.Add($"out of reach ±8 m {outOfReach}");
        if (skipped.Count > 0) text.AppendLine("skipped: " + string.Join(" · ", skipped));

        text.Append($"{Hint(narrowerKey)}/{Hint(widerKey)} width · {Hint(shorterKey)}/{Hint(longerKey)} length · " +
                    $"{Hint(lockKey)} lock · {Hint(raiseKey)}/{Hint(lowerKey)} height");
        return text.ToString();
    }

    private static string Hint(ConfigEntry<KeyboardShortcut> key) => key.Value.ToString();

    private void BeginLevel(Player player)
    {
        if (working || plan == null || budget == null) return;
        if (cost.Vertices == 0)
        {
            if (shortage == Shortage.Stamina) Hud.instance.StaminaBarEmptyFlash();
            Refuse(player, shortage != Shortage.None ? Capitalise(ShortageText(shortage))
                : plan.Count(VertexState.NeedsPickaxe) > 0 ? "You need a pickaxe in your inventory" : "The area is already level");
            return;
        }
        if (Blocker(player, plan) is { } blocked) { Refuse(player, blocked); return; }
        StartCoroutine(LevelRoutine(player, plan.Area, plan.Target, plan.FreeFill));
    }

    /// <summary>Why the area cannot be modified, or null. Checks every vertex the write touches.</summary>
    private static string Blocker(Player player, AreaPlan area)
    {
        foreach (var v in TerrainWriter.Touched(area))
        {
            if (Location.IsInsideNoBuildLocation(v.Point)) return "$msg_nobuildzone";
            if (!PrivateArea.CheckAccess(v.Point, 0, flash: false))
            {
                PrivateArea.CheckAccess(v.Point);
                return "$msg_privatezone";
            }
        }
        return OtherPlayerInTheWay(player, area) ? "Another player is standing in the area" : null;
    }

    private static bool OtherPlayerInTheWay(Player self, AreaPlan area)
    {
        foreach (var other in Player.GetAllPlayers())
        {
            if (!other || other == self) continue;
            var position = other.transform.position;
            foreach (var v in area.Vertices)
            {
                if (!v.Selected || v.Rise <= PlayerSafeRise) continue;
                var offset = v.Point - position;
                if (offset.x * offset.x + offset.z * offset.z < PlayerClearance * PlayerClearance) return true;
            }
        }
        return false;
    }

    /// <summary>Two-phase write because of races with another zone owner; the cost is paid only after a successful write.</summary>
    private IEnumerator LevelRoutine(Player player, RectInt area, float target, bool freeFill)
    {
        working = true;
        var hoe = player.GetRightItem();
        player.m_lastToolUseTime = Time.time + ClaimWait;
        try
        {
            var claimed = TerrainWriter.Claim(plan);
            if (claimed == null) { Refuse(player, "The terrain cannot be modified here right now"); yield break; }
            yield return new WaitForSeconds(ClaimWait);

            if (!player || player != Player.m_localPlayer || player.IsDead() || player.GetRightItem() != hoe) yield break;
            if (!TerrainWriter.Ready(claimed)) { Refuse(player, "Another player is modifying the terrain here"); yield break; }

            // Terrain, resources and nearby players may have changed while waiting: measure and check again.
            var fresh = Budget.Of(player, rates, pickaxeSeconds.Value, staminaDiscount.Value);
            var written = AreaPlan.Measure(area, target, freeFill, fresh.Pickaxe != null, player.m_placementGhost, pieceMask);
            var (paid, lack) = fresh.Select(written, player.transform.position, rates, v => TerrainWriter.Covers(claimed, v));
            if (paid.Vertices == 0) { Refuse(player, lack != Shortage.None ? Capitalise(ShortageText(lack)) : "The area is already level"); yield break; }
            if (Blocker(player, written) is { } blocked) { Refuse(player, blocked); yield break; }

            try
            {
                TerrainWriter.Write(written, claimed, preserveSoil.Value);
            }
            finally
            {
                Charge(player, fresh, paid);
            }
            player.FaceLookDirection();
            player.m_zanim.SetTrigger(hoe.m_shared.m_attack.m_attackAnimation);
            PlayEffects(player, hoe, fresh, paid, written.Centre);
            player.m_lastToolUseTime = Time.time + Mathf.Max(0, paid.Seconds - player.m_placeDelay);
            plan = null;
            StartCoroutine(ResetGrassLater(written));
        }
        finally
        {
            working = false;
        }
    }

    /// <summary>
    /// Sounds and particles of the vanilla actions the click stands for: the Level ground swing always (Piece.m_placeEffect
    /// is what vanilla PlacePiece plays, the TerrainOp effect is the dust), Raise ground and a pickaxe hit when they were used.
    /// </summary>
    private void PlayEffects(Player player, ItemDrop.ItemData hoe, Budget paidFrom, Cost paid, Vector3 centre)
    {
        var rotation = Quaternion.identity;
        rates.LevelPiece.m_placeEffect.Create(centre, rotation);
        rates.LevelOp.m_onPlacedEffect.Create(centre, rotation);
        hoe.m_shared.m_buildEffect.Create(player.transform.position, rotation);
        if (paid.RaiseSwings > 0)
        {
            rates.RaisePiece.m_placeEffect.Create(centre, rotation);
            rates.RaiseOp.m_onPlacedEffect.Create(centre, rotation);
        }
        if (paid.PickHits > 0)
        {
            // Attack.DoMeleeAttack plays both the weapon's and the attack's terrain hit effect.
            paidFrom.Pickaxe.m_shared.m_hitTerrainEffect.Create(centre, rotation);
            paidFrom.Pickaxe.m_shared.m_attack.m_hitTerrainEffect.Create(centre, rotation);
        }
    }

    private void Charge(Player player, Budget paidFrom, Cost paid)
    {
        player.UseStamina(paid.Stamina);
        if (paidFrom.Hoe.m_shared.m_useDurability)
            paidFrom.Hoe.m_durability = Mathf.Max(0, paidFrom.Hoe.m_durability - paid.HoeWear);
        if (paid.PickHits > 0 && paidFrom.Pickaxe.m_shared.m_useDurability)
            paidFrom.Pickaxe.m_durability = Mathf.Max(0, paidFrom.Pickaxe.m_durability - paid.PickWear);
        if (paid.Stone > 0 && !player.m_noPlacementCost)
            player.GetInventory().RemoveItem(rates.Stone.m_itemData.m_shared.m_name, paid.Stone);
        if (paid.Returned > 0) GiveDugItems(player, paidFrom.Yield.Item, paid.Returned);
    }

    /// <summary>Dug-up material goes straight to the inventory; what does not fit drops at the feet (vanilla throws it on the ground).</summary>
    private static void GiveDugItems(Player player, ItemDrop item, int amount)
    {
        var inventory = player.GetInventory();
        int fits = 0;
        while (fits < amount && inventory.CanAddItem(item.gameObject, fits + 1)) fits++;
        if (fits > 0) inventory.AddItem(item.gameObject, fits);
        int maxStack = Mathf.Max(1, item.m_itemData.m_shared.m_maxStackSize);
        for (int left = amount - fits; left > 0; left -= maxStack)
        {
            var dropped = Instantiate(item.gameObject, player.transform.position + Vector3.up, Quaternion.identity);
            dropped.GetComponent<ItemDrop>().m_itemData.m_stack = Mathf.Min(left, maxStack);
        }
        player.Message(MessageHud.MessageType.TopLeft, $"+{amount} {Localization.instance.Localize(item.m_itemData.m_shared.m_name)}");
    }

    private IEnumerator ResetGrassLater(AreaPlan levelled)
    {
        yield return null;
        yield return null;
        if (ClutterSystem.instance) ClutterSystem.instance.ResetGrass(levelled.Centre, levelled.HalfDiagonal + 1);
    }

    private static string Capitalise(string text) => text.Length == 0 ? text : char.ToUpper(text[0]) + text.Substring(1);

    private static void Refuse(Player player, string message) => player.Message(MessageHud.MessageType.Center, message);

    [HarmonyPatch(typeof(Player), nameof(Player.TryPlacePiece))]
    private static class TryPlacePiecePatch
    {
        /// <summary>
        /// Invalid placement (snow, dungeon, ward at the cursor…) is left to vanilla, which shows its message and returns false.
        /// Valid placement is handled by the mod and returns false, so vanilla charges nothing; cost and animation are done in <see cref="LevelRoutine"/>.
        /// </summary>
        private static bool Prefix(Player __instance, Piece piece, ref bool __result)
        {
            if (!IsAreaPiece(piece)) return true;
            if (__instance != Player.m_localPlayer) { __result = false; return false; }
            __instance.UpdatePlacementGhost(flashGuardStone: true);
            if (__instance.m_placementStatus != Player.PlacementStatus.Valid) return true;
            instance.BeginLevel(__instance);
            __result = false;
            return false;
        }
    }
}
