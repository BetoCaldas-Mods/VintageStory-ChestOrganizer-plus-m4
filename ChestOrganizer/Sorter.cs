using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace ChestOrganizer;
public static class Sorter {
    public static void Sort(this IInventory inventory, IComparer<ItemStack> comparer, ICoreClientAPI api, bool merge = true) {
        int n = inventory.Count;
        var current = Enumerable.Range(0, n).ToArray();
        var from = current.ToArray();
        var order = current
            .Order(new SlotIndexComparer(inventory, comparer))
            .ToArray();
        var player = api.World.Player;
        var manager = player.InventoryManager;
        var ops = new InventoryOps(inventory, n, api, player, manager);

        for (int i = 0; i < n; i++) {
            int j = order[i];
            int k = current[j];
            if (k == i) continue;

            if (!ops.RearrangeSlots(inventory[i], inventory[k])) continue;

            (from[i], from[k]) = (from[k], from[i]);
            current[from[i]] = i;
            current[from[k]] = k;
        }

        if (!merge) return;
        ops.MergeAdjacentStacks(n);
    }

    private sealed class InventoryOps {
        private readonly IInventory inventory;
        private readonly int slotCount;
        private readonly ICoreClientAPI api;
        private readonly IPlayer player;
        private readonly IPlayerInventoryManager manager;

        public InventoryOps(IInventory inventory, int slotCount, ICoreClientAPI api, IPlayer player, IPlayerInventoryManager manager) {
            this.inventory = inventory;
            this.slotCount = slotCount;
            this.api = api;
            this.player = player;
            this.manager = manager;
        }

        public bool RearrangeSlots(ItemSlot target, ItemSlot source) {
            if (target.Empty) {
                return !source.Empty && Transfer(source, target);
            }
            if (source.Empty) {
                return Transfer(target, source);
            }
            return SwapSlots(source, target);
        }

        public void MergeAdjacentStacks(int n) {
            bool changed = false;
            for (int i = 0, j = 1; i < n - 1 && j < n; ) {
                var target = inventory[i];
                var source = inventory[j];
                if (changed && target.CanTakeFrom(source)) {
                    changed = Transfer(source, target);
                    if (source.Empty) j++;
                } else {
                    changed = true;
                    i++;
                    if (i >= j) j = i + 1;
                }
            }
        }

        private bool Transfer(ItemSlot from, ItemSlot to) {
            int pre = from.StackSize;
            int amount = from.GetRemainingSlotSpace(to.Itemstack);
            var op = new ItemStackMoveOperation(api.World, EnumMouseButton.Left, 0, EnumMergePriority.AutoMerge, amount) {
                ActingPlayer = player,
            };
            SendPacket(manager.TryTransferTo(from, to, ref op));
            return pre != from.StackSize;
        }

        private bool SwapSlots(ItemSlot a, ItemSlot b) {
            try {
                if (a.TryFlipWith(b)) {
                    SendPacket(a.Inventory.InvNetworkUtil.GetFlipSlotsPacket(b.Inventory, SlotId(b), SlotId(a)));
                    return true;
                }
            } catch (ArgumentException) {
            }
            return SwapViaEmptySlot(a, b);
        }

        private bool SwapViaEmptySlot(ItemSlot a, ItemSlot b) {
            var temp = FindEmptySlot(a, b);
            if (temp == null) return false;
            if (!Transfer(a, temp)) return false;
            if (!Transfer(b, a)) {
                Transfer(temp, a);
                return false;
            }
            return Transfer(temp, b);
        }

        private ItemSlot FindEmptySlot(ItemSlot a, ItemSlot b) {
            for (int i = 0; i < slotCount; i++) {
                var slot = inventory[i];
                if (slot.Empty && slot != a && slot != b) return slot;
            }
            return null;
        }

        private static int SlotId(ItemSlot slot) => slot.Inventory.GetSlotId(slot);

        private void SendPacket(object obj) {
            if (obj is Packet_Client packet) {
                api.Network.SendPacketClient(packet);
            }
        }
    }

    private sealed class SlotIndexComparer : IComparer<int> {
        private readonly IInventory inventory;
        private readonly IComparer<ItemStack> comparer;

        public SlotIndexComparer(IInventory inventory, IComparer<ItemStack> comparer) {
            this.inventory = inventory;
            this.comparer = comparer;
        }

        public int Compare(int x, int y)
            => comparer.Compare(inventory[x].Itemstack, inventory[y].Itemstack);
    }
}
