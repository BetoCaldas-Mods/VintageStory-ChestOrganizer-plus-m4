using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace ChestOrganizer;
public class Main : ModSystem {
    public const string ID = "chestorganizer";
    public const string Hotkey = ID + ".openall";

    private static Harmony harmony;

    private ICoreClientAPI api;

    public override void StartPre(ICoreAPI api) 
        => (harmony ??= new Harmony(ID)).PatchAll();

    public override void Dispose() 
        => harmony?.UnpatchAll(ID);

    public override void StartClientSide(ICoreClientAPI api) {
        this.api = api;
        Patch_ChestDialog.Setup(api);
        Icons.Setup(api);

        api.Input.RegisterHotKey(
            Hotkey,
            Lang.Get("chestorganizer:openall"),
            GlKeys.R,
            HotkeyType.CharacterControls);
        api.Input.SetHotKeyHandler(Hotkey, OpenAll);
    }

    public bool OpenAll(KeyCombination _) {
        var player = api.World.Player;
        if (player.WorldData.CurrentGameMode == EnumGameMode.Creative) return false;

        var openMerged = api.OpenedGuis.OfType<GuiDialogMergedInventory>().FirstOrDefault();
        if (openMerged != null) {
            openMerged.TryClose();
            return true;
        }

        MergedInventory.SyncStaticState(api);

        float range = player.WorldData.PickingRange + 1;
        float rangesq = range * range;
        var eyePos = player.Entity.SidedPos.XYZ.Add(player.Entity.LocalEyePos - 0.5f);
        var reinforcement = api.ModLoader.GetModSystem<ModSystemBlockReinforcement>();
        var containers = CollectContainersInRange(player, range, rangesq, eyePos, reinforcement);
        var chests = PrepareContainersForMerge(player, containers);

        if (chests.Count > 0) {
            MergedInventory.MergeRange(chests, api);
        }

        return true;
    }

    private ContainerScan CollectContainersInRange(
        IPlayer player,
        float range,
        float rangesq,
        Vec3d eyePos,
        ModSystemBlockReinforcement reinforcement) {
        var scan = new ContainerScan();
        var accessor = api.World.BlockAccessor;
        accessor.WalkBlocks(
            (eyePos - range).AsBlockPos,
            (eyePos + (range + 1.0f)).AsBlockPos,
            (Block b, int x, int y, int z) => StepContainer(player, rangesq, eyePos, reinforcement, scan, x, y, z));
        return scan;
    }

    private void StepContainer(
        IPlayer player,
        float rangesq,
        Vec3d eyePos,
        ModSystemBlockReinforcement reinforcement,
        ContainerScan scan,
        int x,
        int y,
        int z) {
        if (eyePos.SquareDistanceTo(x, y, z) > rangesq) return;
        var pos = new BlockPos(x, y, z);
        var entity = api.World.BlockAccessor.GetBlockEntity<BlockEntityGenericTypedContainer>(pos);
        if (entity == null) return;
        if (reinforcement.IsLockedForInteract(pos, player)) return;
        if (IsClaimRestricted(pos, player)) return;
        if (entity.Inventory.HasOpened(player)) {
            scan.OpenedInRange.Add(entity);
            return;
        }
        scan.ToOpen.Add(entity);
    }

    private List<BlockEntityGenericTypedContainer> PrepareContainersForMerge(
        IPlayer player,
        ContainerScan scan) {
        if (scan.ToOpen.Count > 0) {
            return scan.ToOpen;
        }
        if (scan.OpenedInRange.Count == 0 || MergedInventory.IsMergedDialogVisible(api)) {
            return scan.ToOpen;
        }

        var openSingleChestPositions = api.OpenedGuis
            .OfType<GuiDialogBlockEntityInventory>()
            .Select(dialog => dialog.BlockEntityPosition)
            .ToHashSet();
        var ghosts = scan.OpenedInRange
            .Where(entity => !openSingleChestPositions.Contains(entity.Pos))
            .ToList();
        if (ghosts.Count == 0) {
            return scan.ToOpen;
        }

        MergedInventory.RecoverGhostContainers(api, player, ghosts, openSingleChestPositions);
        return ghosts;
    }

    private sealed class ContainerScan {
        public List<BlockEntityGenericTypedContainer> ToOpen { get; } = new();
        public List<BlockEntityGenericTypedContainer> OpenedInRange { get; } = new();
    }

    private bool IsClaimRestricted(BlockPos pos, IPlayer player) {
        try {
            var claims = api.World.Claims;
            if (claims == null) return false;
            var atPos = claims.Get(pos);
            if (atPos == null || atPos.Length == 0) return false;
            return claims.TestAccess(player, pos, EnumBlockAccessFlags.Use) != EnumWorldAccessResponse.Granted;
        } catch {
            return false;
        }
    }
}
