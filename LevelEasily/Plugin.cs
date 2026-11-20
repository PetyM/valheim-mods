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
    private ConfigEntry<float> step, pickaxeSeconds;
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
        lockKey = Config.Bind("Klávesy", "Zamknout", new KeyboardShortcut(KeyCode.L),
            "Zamkne cílovou výšku (horní plocha kusu stavby pod kurzorem, jinak terén pod hráčem) a mřížku plochy. Druhé stisknutí odemkne.");
        raiseKey = Config.Bind("Klávesy", "Zvýšit cíl", new KeyboardShortcut(KeyCode.PageUp));
        lowerKey = Config.Bind("Klávesy", "Snížit cíl", new KeyboardShortcut(KeyCode.PageDown));
        widerKey = Config.Bind("Klávesy", "Širší", new KeyboardShortcut(KeyCode.RightArrow));
        narrowerKey = Config.Bind("Klávesy", "Užší", new KeyboardShortcut(KeyCode.LeftArrow));
        longerKey = Config.Bind("Klávesy", "Delší", new KeyboardShortcut(KeyCode.UpArrow));
        shorterKey = Config.Bind("Klávesy", "Kratší", new KeyboardShortcut(KeyCode.DownArrow));
        step = Config.Bind("Klávesy", "Krok výšky", 0.1f, "O kolik metrů posune cíl jedno stisknutí.");
        maxSize = Config.Bind("Plocha", "Největší strana", 16,
            new ConfigDescription("Největší délka strany plochy v metrech.", new AcceptableValueRange<int>(1, 32)));
        preserveSoil = Config.Bind("Plocha", "Zachovat obdělanou a dlážděnou půdu", true,
            "Obdělanou a dlážděnou půdu srovná, ale nepřetře hlínou.");
        pickaxeSeconds = Config.Bind("Cena", "Čas úderu krumpáčem", 1f,
            new ConfigDescription("Na kolik sekund zablokuje motyku jeden započítaný úder krumpáčem.", new AcceptableValueRange<float>(0f, 5f)));
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
            Name = "Srovnat plochu",
            Description = "Srovná obdélník přichycený na mřížku metrů na cílovou výšku jedním kliknutím. " +
                          "Stojí tolik, kolik by stála ruční práce: švihy motyky, údery krumpáčem z inventáře a kámen za dosypání.\n" +
                          "Šipky: velikost · L: zamknout výšku a mřížku · PageUp/PageDown: posunout cíl",
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

        budget = Budget.Of(player, rates, pickaxeSeconds.Value);
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

    /// <summary>Jako vanilla Level ground: terén pod hráčem, ne výška nohou (skok, plavání, podlaha).</summary>
    private static float GroundUnder(Player player) => ZoneSystem.instance.GetGroundHeight(player.transform.position);

    private float AimedHeight(Player player)
    {
        var camera = GameCamera.instance.transform;
        if (!Physics.Raycast(camera.position, camera.forward, out var hit, AimDistance, aimMask) ||
            !hit.collider.GetComponentInParent<Piece>())
            return GroundUnder(player);
        // Horní plocha zasaženého kusu v místě zásahu, i když kurzor míří na jeho bok.
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
        Shortage.Stamina => "málo výdrže",
        Shortage.HoeWear => "motyka by se rozbila",
        Shortage.PickaxeWear => "krumpáč by se rozbil",
        Shortage.Stone => "málo kamene",
        _ => "",
    };

    private string Readout()
    {
        var text = new StringBuilder();
        text.Append($"{size.x} × {size.y} m · cíl {plan.Target:0.00} m");
        if (lockedHeight.HasValue) text.Append(" · zamčeno");
        if (plan.Target < ZoneSystem.instance.m_waterLevel) text.Append(" · pod hladinou moře");
        text.AppendLine();

        int wanted = plan.Vertices.Count(v => v.Wanted);
        if (working) text.AppendLine("upravuji terén…");
        else if (wanted == 0) text.AppendLine("není co srovnat");
        else
        {
            text.Append($"motyka {cost.AllSwings}× · krumpáč {cost.PickHits}× · kámen {cost.Stone} · výdrž {cost.Stamina:0} · {cost.Seconds:0.#} s");
            text.AppendLine();
            text.Append($"opotřebení: motyka {cost.HoeWear:0.#}");
            if (cost.PickHits > 0) text.Append($", {Localization.instance.Localize(budget.Pickaxe.m_shared.m_name)} {cost.PickWear:0.#}");
            if (cost.Vertices < wanted) text.Append($" · srovná se {cost.Vertices} z {wanted} bodů ({ShortageText(shortage)})");
            text.AppendLine();
        }

        var skipped = new List<string>();
        int underPiece = plan.Count(VertexState.UnderPiece), noPickaxe = plan.Count(VertexState.NeedsPickaxe);
        int outOfReach = plan.Vertices.Count(v => v.Unreachable);
        if (underPiece > 0) skipped.Add($"pod stavbou {underPiece}");
        if (noPickaxe > 0) skipped.Add($"chybí krumpáč {noPickaxe}");
        if (outOfReach > 0) skipped.Add($"mimo dosah ±8 m {outOfReach}");
        if (skipped.Count > 0) text.AppendLine("vynecháno: " + string.Join(" · ", skipped));

        text.Append($"{Hint(narrowerKey)}/{Hint(widerKey)} šířka · {Hint(shorterKey)}/{Hint(longerKey)} délka · " +
                    $"{Hint(lockKey)} zamknout · {Hint(raiseKey)}/{Hint(lowerKey)} výška");
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
                : plan.Count(VertexState.NeedsPickaxe) > 0 ? "Bez krumpáče v inventáři to nejde" : "Plocha už je rovná");
            return;
        }
        if (Blocker(player, plan) is { } blocked) { Refuse(player, blocked); return; }
        StartCoroutine(LevelRoutine(player, plan.Area, plan.Target, plan.FreeFill));
    }

    /// <summary>Důvod, proč plochu nejde upravit, nebo null. Kontroluje každý vrchol, kterého se zápis dotkne.</summary>
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
        return OtherPlayerInTheWay(player, area) ? "V ploše stojí jiný hráč" : null;
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

    /// <summary>Zápis ve dvou fázích kvůli souběhu s jiným vlastníkem zóny, platí se až po úspěšném zápisu.</summary>
    private IEnumerator LevelRoutine(Player player, RectInt area, float target, bool freeFill)
    {
        working = true;
        var hoe = player.GetRightItem();
        player.m_lastToolUseTime = Time.time + ClaimWait;
        try
        {
            var claimed = TerrainWriter.Claim(plan);
            if (claimed == null) { Refuse(player, "Terén tady teď nejde upravit"); yield break; }
            yield return new WaitForSeconds(ClaimWait);

            if (!player || player != Player.m_localPlayer || player.IsDead() || player.GetRightItem() != hoe) yield break;
            if (!TerrainWriter.Ready(claimed)) { Refuse(player, "Terén tu právě upravuje jiný hráč"); yield break; }

            // Za čekání se mohl změnit terén, zdroje i hráči kolem: spočítat a zkontrolovat znovu.
            var fresh = Budget.Of(player, rates, pickaxeSeconds.Value);
            var written = AreaPlan.Measure(area, target, freeFill, fresh.Pickaxe != null, player.m_placementGhost, pieceMask);
            var (paid, lack) = fresh.Select(written, player.transform.position, rates, v => TerrainWriter.Covers(claimed, v));
            if (paid.Vertices == 0) { Refuse(player, lack != Shortage.None ? Capitalise(ShortageText(lack)) : "Plocha už je rovná"); yield break; }
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
            rates.LevelOp.m_onPlacedEffect.Create(written.Centre, Quaternion.identity);
            hoe.m_shared.m_buildEffect.Create(player.transform.position, Quaternion.identity);
            player.m_lastToolUseTime = Time.time + Mathf.Max(0, paid.Seconds - player.m_placeDelay);
            plan = null;
            StartCoroutine(ResetGrassLater(written));
        }
        finally
        {
            working = false;
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
        /// Neplatné umístění (sníh, dungeon, ward u kurzoru…) nechá na vanilla, ta vypíše hlášku a vrátí false.
        /// Platné vyřídí mód a vrátí false, takže vanilla nic nestrhne; cenu a animaci řeší <see cref="LevelRoutine"/>.
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
