// All comments in English as requested
using GTANetworkAPI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Spirit.Core.Bootstrap;              // SpiritHost.Get<T>()
using Spirit.Core.Entities;
using Spirit.Data;                       // <-- wichtig: enthält SpiritDbContext
using Spirit.Data.Inventar;              // deine Inventar-Entities
using System.Text.Json;

public class InvItemDto
{
    public int id;
    public string defKey = "";
    public int qty;
    public int x;
    public int y;
    public float weight; // qty * def.WeightPerUnit
    public string equipSlot { get; set; } = "";

}

public class EquipDto { public string slot = ""; public string defKey = ""; public float carryBonusKg = 0f; }

public class InventorySnapshotDto
{
    public int invId;
    public string ownerType = "player"; // "player"/"vehicle"
    public long ownerId;
    public int w, h;
    public float capacityKg;
    public float currentKg;
    public List<InvItemDto> items = new();
    public List<EquipDto> equip = new();
}

public sealed class InventoryApplyLayoutDto
{
    public int invId { get; set; }
    public List<InventoryApplyItemDto> items { get; set; } = new();
}
public sealed class InventoryApplyItemDto
{
    public int id { get; set; }                // <=0 => neu anlegen
    public string defKey { get; set; } = "";
    public int qty { get; set; }
    public int index { get; set; }             // linear index (für (x,y))
    public int x { get; set; }                 // optional (falls mitgesendet)
    public int y { get; set; }
}


public class InventoryService : Script
{
    // Get the factory the same way as in DiscordEvents
    private static readonly JsonSerializerOptions InvJson = new JsonSerializerOptions
    {
        IncludeFields = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // All comments in English as requested
    private static readonly HashSet<string> AllowedSlots =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
    "hat","mask","glasses","earRings","chain","tShirt","top","backpack",
    "wallet","armour","watch","gloves","pants","shoes"
    };


    private static IDbContextFactory<SpiritDbContext> RequireDbf()
    {
        var sp = SpiritHost.Services;
        if (sp == null)
            throw new System.InvalidOperationException(
                "[InventoryService] DI not initialized. Make sure Bootstrap set SpiritHost.Services before using inventory.");
        return sp.GetRequiredService<IDbContextFactory<SpiritDbContext>>();
    }

    [RemoteEvent("server:inv:applyLayout")]
    public void ApplyLayout(Player player, string json)
    {
        if (player == null || string.IsNullOrWhiteSpace(json)) return;

        Task.Run(async () =>
        {
            var dbf = RequireDbf();
            await using var db = await dbf.CreateDbContextAsync();

            InventoryApplyLayoutDto dto;
            try { dto = JsonSerializer.Deserialize<InventoryApplyLayoutDto>(json) ?? new(); }
            catch { return; }

            // Sicherheitscheck: gehört das Inv dem Spieler?
            var sp = player.AsSPlayer();
            var inv = await db.Inventories
                .Include(i => i.Items).ThenInclude(ii => ii.ItemDefinition)
                .FirstOrDefaultAsync(i => i.Id == dto.invId && i.OwnerType == InventoryOwnerType.Player && i.OwnerId == sp.CharacterId);
            if (inv == null) return;

            var w = Math.Max(1, inv.GridW);
            var payloadIds = new HashSet<int>();

            // Map defKey → def.Id
            var keys = dto.items.Select(i => i.defKey).Distinct().ToArray();
            var defs = await db.ItemDefinitions.Where(d => keys.Contains(d.Key)).ToDictionaryAsync(d => d.Key, d => d);

            // Update / Create
            foreach (var it in dto.items)
            {
                var qty = Math.Max(0, it.qty);
                var idx = Math.Max(0, it.index);
                var x = it.x >= 0 ? it.x : (idx % w);
                var y = it.y >= 0 ? it.y : (idx / w);

                if (it.id > 0)
                {
                    var row = inv.Items.FirstOrDefault(r => r.Id == it.id);
                    if (row == null) continue;
                    row.SlotX = x;
                    row.SlotY = y;
                    row.Quantity = qty;
                    payloadIds.Add(row.Id);
                }
                else
                {
                    if (!defs.TryGetValue(it.defKey ?? "", out var def)) continue;
                    var row = new InventoryItem
                    {
                        InventoryId = inv.Id,
                        ItemDefinitionId = def.Id,
                        SlotX = x,
                        SlotY = y,
                        Quantity = qty
                    };
                    db.InventoryItems.Add(row);
                    await db.SaveChangesAsync(); // damit row.Id existiert
                    payloadIds.Add(row.Id);
                }
            }

            // Remove DB-Items, die NICHT im Payload sind (Client hat gelöscht/vereinigt)
            var toRemove = inv.Items.Where(r => !payloadIds.Contains(r.Id)).ToList();
            if (toRemove.Count > 0)
            {
                db.InventoryItems.RemoveRange(toRemove);
            }

            await db.SaveChangesAsync();

            // Optional: Snapshot zurückschicken – NICHT nötig, Client ist autoritativ
            // Wenn du willst, kannst du einen minimalen Ack senden
            NAPI.Task.Run(() =>
            {
                if (player != null && player.Exists)
                    player.TriggerEvent("client:inv:ack", "layout-ok");
            });
        });
    }

    [RemoteEvent("server:inv:open")]
    public void OpenInv(Player player, int targetType = 0, long targetId = 0)
    {
        Task.Run(async () =>
        {
            var dbf = RequireDbf();
            await using var db = await dbf.CreateDbContextAsync();

            var inv = await EnsurePlayerInventoryAsync(db, player);
            var snap = await BuildSnapshotAsync(db, inv);

            var json = JsonSerializer.Serialize(snap, InvJson);   // <- Options nutzen
            NAPI.Task.Run(() =>
            {
                if (player == null || !player.Exists) return;
                player.TriggerEvent("client:inv:open", json);
            });
        });
    }

    [RemoteEvent("server:inv:equip")]
    public void Equip(Player player, int itemId, string slot)
    {
        Task.Run(async () =>
        {
            var dbf = RequireDbf();
            await using var db = await dbf.CreateDbContextAsync();

            var inv = await EnsurePlayerInventoryAsync(db, player);
            var it = await db.InventoryItems.Include(i => i.ItemDefinition)
                         .FirstOrDefaultAsync(i => i.Id == itemId && i.InventoryId == inv.Id);
            if (it == null) return;

            var wanted = (slot ?? "").Trim();
            if (!AllowedSlots.Contains(wanted)) return;

            // item muss in diesen Slot passen
            var defSlot = (it.ItemDefinition?.EquipSlot ?? "").Trim();
            if (!string.Equals(defSlot, wanted, StringComparison.OrdinalIgnoreCase)) return;

            await EquipItemAsync(db, inv, it);
            await db.SaveChangesAsync();

            var snap = await BuildSnapshotAsync(db, inv);
            var json = JsonSerializer.Serialize(snap, InvJson);
            NAPI.Task.Run(() => { if (player?.Exists == true) player.TriggerEvent("client:inv:update", json); });
        });
    }

    [RemoteEvent("server:inv:unequip")]
    public void Unequip(Player player, string slot)
    {
        Task.Run(async () =>
        {
            var dbf = RequireDbf();
            await using var db = await dbf.CreateDbContextAsync();

            var inv = await EnsurePlayerInventoryAsync(db, player);
            var eq = await db.EquipmentSlots.FirstOrDefaultAsync(e => e.InventoryId == inv.Id && e.Slot == slot);
            if (eq == null || eq.ItemDefinitionId == null) return;

            // zurück ins Grid
            await PlaceBackToGridAsync(db, inv, eq.ItemDefinitionId.Value);
            eq.ItemDefinitionId = null;

            await db.SaveChangesAsync();
            var snap = await BuildSnapshotAsync(db, inv);
            var json = JsonSerializer.Serialize(snap, InvJson);
            NAPI.Task.Run(() => { if (player?.Exists == true) player.TriggerEvent("client:inv:update", json); });
        });
    }

    [RemoteEvent("server:inv:drop")]
    public void Drop(Player player, int itemId, int amount)
    {
        Task.Run(async () =>
        {
            try
            {
                var dbf = RequireDbf();
                await using var db = await dbf.CreateDbContextAsync();

                var inv = await EnsurePlayerInventoryAsync(db, player);
                var it = await db.InventoryItems
                    .Include(i => i.ItemDefinition)
                    .FirstOrDefaultAsync(i => i.Id == itemId && i.InventoryId == inv.Id);

                if (it == null) return;

                int take = Math.Max(1, amount);
                if (take >= it.Quantity)
                {
                    db.InventoryItems.Remove(it);
                }
                else
                {
                    it.Quantity -= take;
                }

                // TODO: Ground-Spawn (später)
                await db.SaveChangesAsync();

                var snap = await BuildSnapshotAsync(db, inv);
                NAPI.Task.Run(() =>
                {
                    if (player == null || !player.Exists) return;
                    player.TriggerEvent("client:inv:update", JsonSerializer.Serialize(snap));
                });
            }
            catch { }
        });
    }

    [RemoteEvent("server:inv:split")]
    public void Split(Player player, int itemId, int amount)
    {
        if (player == null || amount <= 0) return;

        Task.Run(async () =>
        {
            var dbf = RequireDbf();
            await using var db = await dbf.CreateDbContextAsync();

            var inv = await EnsurePlayerInventoryAsync(db, player);
            if (inv == null) return;

            var item = await db.InventoryItems
                .Include(i => i.ItemDefinition)
                .FirstOrDefaultAsync(i => i.InventoryId == inv.Id && i.Id == itemId);

            if (item == null) return;

            var def = item.ItemDefinition;
            if (def == null || def.MaxStack <= 1) return; // nicht teilbar
            if (item.Quantity <= 1) return;               // nichts abzuzweigen

            // Menge clampen (mind. 1 im Ursprung übrig lassen)
            amount = Math.Min(amount, item.Quantity - 1);
            if (amount <= 0) return;

            int remaining = amount;

            // 1) In bestehende, nicht volle Stacks desselben Items einsortieren
            var stacks = await db.InventoryItems
                .Include(s => s.ItemDefinition)
                .Where(s => s.InventoryId == inv.Id
                         && s.ItemDefinitionId == item.ItemDefinitionId
                         && s.Id != item.Id
                         && s.Quantity < s.ItemDefinition.MaxStack)
                .OrderBy(s => s.SlotY).ThenBy(s => s.SlotX)
                .ToListAsync();

            foreach (var s in stacks)
            {
                int can = s.ItemDefinition.MaxStack - s.Quantity;
                int take = Math.Min(can, remaining);
                if (take > 0)
                {
                    s.Quantity += take;
                    remaining -= take;
                    if (remaining == 0) break;
                }
            }

            // 2) Neue Stacks in freie Zellen legen (sofern noch Rest)
            while (remaining > 0)
            {
                var free = await FindFirstFreeCellAsync(db, inv);
                if (!free.HasValue) break;
                var (fx, fy) = free.Value;

                int add = Math.Min(def.MaxStack, remaining);
                db.InventoryItems.Add(new InventoryItem
                {
                    InventoryId = inv.Id,
                    ItemDefinitionId = def.Id,
                    SlotX = fx,
                    SlotY = fy,
                    Quantity = add
                });
                remaining -= add;
            }

            // 3) Ursprungsstack reduzieren um tatsächlich platzierten Anteil
            int placed = amount - remaining;
            if (placed > 0)
            {
                item.Quantity -= placed;
                if (item.Quantity < 1) item.Quantity = 1;
                await db.SaveChangesAsync();

                var snap = await BuildSnapshotAsync(db, inv);
                NAPI.Task.Run(() =>
                {
                    if (player != null && player.Exists)
                        player.TriggerEvent("client:inv:update", System.Text.Json.JsonSerializer.Serialize(snap));
                });
            }
            // Wenn remaining > 0: kein Platz – optional Toast/Notify schicken
        });
    }


    [RemoteEvent("server:inv:move")]
    public void Move(Player player, int itemId, int toX, int toY)
    {
        Task.Run(async () =>
        {
            var dbf = RequireDbf();
            await using var db = await dbf.CreateDbContextAsync();

            var inv = await EnsurePlayerInventoryAsync(db, player);
            var item = await db.InventoryItems
                .Include(i => i.ItemDefinition)
                .FirstOrDefaultAsync(i => i.Id == itemId && i.InventoryId == inv.Id);
            if (item == null) return;

            var dest = await db.InventoryItems
                .Include(i => i.ItemDefinition)
                .FirstOrDefaultAsync(i => i.InventoryId == inv.Id && i.SlotX == toX && i.SlotY == toY);

            if (dest == null)
            {
                item.SlotX = toX; item.SlotY = toY;
            }
            else if (dest.ItemDefinitionId == item.ItemDefinitionId && dest.Quantity < dest.ItemDefinition.MaxStack)
            {
                int can = dest.ItemDefinition.MaxStack - dest.Quantity;
                int move = System.Math.Min(can, item.Quantity);
                dest.Quantity += move;
                item.Quantity -= move;
                if (item.Quantity <= 0) db.InventoryItems.Remove(item);
            }
            else
            {
                (dest.SlotX, item.SlotX) = (item.SlotX, dest.SlotX);
                (dest.SlotY, item.SlotY) = (item.SlotY, dest.SlotY);
            }

            await db.SaveChangesAsync();

            var snap = await BuildSnapshotAsync(db, inv);
            var json = JsonSerializer.Serialize(snap, InvJson);   // <- Options nutzen
            NAPI.Task.Run(() =>
            {
                if (player == null || !player.Exists) return;
                player.TriggerEvent("client:inv:update", json);
            });
        });
    }

    // ===== Helpers (async, EF via factory) =====

    private async Task EquipItemAsync(SpiritDbContext db, Inventory inv, InventoryItem item)
    {
        var def = item.ItemDefinition ?? await db.ItemDefinitions.FirstOrDefaultAsync(d => d.Id == item.ItemDefinitionId);
        if (def == null) return;

        var rawSlot = (def.EquipSlot ?? "").Trim();
        var slotName = rawSlot; // hier keine Lowercase-Schreibweise erzwingen – Vergleiche sind Case-Insensitive
        if (string.IsNullOrEmpty(slotName) || !AllowedSlots.Contains(slotName))
            return; // not equippable to a known slot

        // Ensure the target slot exists
        var slot = await db.EquipmentSlots.FirstOrDefaultAsync(e => e.InventoryId == inv.Id && e.Slot.Equals(slotName, StringComparison.OrdinalIgnoreCase));
        if (slot == null)
        {
            slot = new EquipmentSlot { InventoryId = inv.Id, Slot = slotName };
            db.EquipmentSlots.Add(slot);
        }

        // If something is already equipped, place it back into grid
        if (slot.ItemDefinitionId != null)
        {
            await PlaceBackToGridAsync(db, inv, slot.ItemDefinitionId.Value);
        }

        // Consume exactly 1 from the stack we equip from
        item.Quantity -= 1;
        if (item.Quantity <= 0)
            db.InventoryItems.Remove(item);

        // Mark new item as equipped
        slot.ItemDefinitionId = def.Id;
    }

    /// <summary>
    /// Place a single unit of the given ItemDefinition back into the inventory grid:
    /// 1) try stacking onto any partial stack,
    /// 2) otherwise spawn a new stack at the first free cell.
    /// Does NOT call SaveChanges; caller is responsible.
    /// </summary>
    private async Task PlaceBackToGridAsync(SpiritDbContext db, Inventory inv, int itemDefId)
    {
        var def = await db.ItemDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == itemDefId);
        if (def == null) return;

        // In bestehende, nicht volle Stacks legen
        var stacks = await db.InventoryItems
            .Where(ii => ii.InventoryId == inv.Id
                      && ii.ItemDefinitionId == itemDefId
                      && ii.Quantity < def.MaxStack)
            .OrderBy(i => i.SlotY).ThenBy(i => i.SlotX)
            .ToListAsync();

        foreach (var s in stacks)
        {
            // hier ist s.Quantity < def.MaxStack garantiert
            s.Quantity += 1;
            return; // SaveChangesAsync macht der Caller
        }

        // Keine Stack-Kapazität -> erste freie Zelle suchen
        var free = await FindFirstFreeCellAsync(db, inv); // nutzt den Overload
        if (free.HasValue)
        {
            var (fx, fy) = free.Value; // erst .Value nehmen, dann dekonstruieren
            db.InventoryItems.Add(new InventoryItem
            {
                InventoryId = inv.Id,
                ItemDefinitionId = itemDefId,
                SlotX = fx,
                SlotY = fy,
                Quantity = 1
            });
            return; // SaveChangesAsync macht der Caller
        }

        // Optional: kein Platz -> später ins Boden-/Container-Inv droppen
    }


    /// <summary>
    /// Finds first free grid cell (x,y) in row-major order. Returns (-1,-1) if full.
    /// </summary>
    private async Task<(int x, int y)?> FindFirstFreeCellAsync(SpiritDbContext db, int invId, int w, int h)
    {
        var used = await db.InventoryItems
            .Where(i => i.InventoryId == invId)
            .Select(i => new { i.SlotX, i.SlotY })
            .ToListAsync();

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (!used.Any(u => u.SlotX == x && u.SlotY == y))
                    return (x, y);
            }
        }
        return null;
    }

    // bequemer Overload, damit du mit (db, inv) aufrufen kannst
    private Task<(int x, int y)?> FindFirstFreeCellAsync(SpiritDbContext db, Inventory inv)
        => FindFirstFreeCellAsync(db, inv.Id, inv.GridW, inv.GridH);

    private async Task<Inventory> EnsurePlayerInventoryAsync(SpiritDbContext db, Player p)
    {
        var sp = p.AsSPlayer();
        var charId = sp?.CharacterId ?? 0;

        var inv = await db.Inventories
            .Include(i => i.Items).ThenInclude(ii => ii.ItemDefinition)
            .Include(i => i.Equipment)
            .FirstOrDefaultAsync(i => i.OwnerType == InventoryOwnerType.Player && i.OwnerId == charId);

        if (inv != null) return inv;

        inv = new Inventory
        {
            OwnerType = InventoryOwnerType.Player,
            OwnerId = charId,
            GridW = 6,
            GridH = 5,
            CapacityKg = 20f
        };
        inv.Equipment.AddRange(new[]
        {
            new EquipmentSlot{ Slot="Backpack" },
            new EquipmentSlot{ Slot="Head" },
            new EquipmentSlot{ Slot="Body" },
            new EquipmentSlot{ Slot="PrimaryWeapon" },
            new EquipmentSlot{ Slot="SecondaryWeapon" }
        });

        db.Inventories.Add(inv);
        await db.SaveChangesAsync();
        return inv;
    }

    private async Task<InventorySnapshotDto> BuildSnapshotAsync(SpiritDbContext db, Inventory inv)
    {
        // make sure relations are loaded
        await db.Entry(inv)
                .Collection(i => i.Items)
                .Query()
                .Include(ii => ii.ItemDefinition)
                .LoadAsync();

        await db.Entry(inv).Collection(i => i.Equipment).LoadAsync();

        var items = inv.Items.Select(i => new InvItemDto
        {
            id = i.Id,
            defKey = i.ItemDefinition.Key,
            qty = i.Quantity,
            x = i.SlotX,
            y = i.SlotY,
            weight = i.Quantity * i.ItemDefinition.WeightPerUnit,
            equipSlot = i.ItemDefinition.EquipSlot ?? ""   // << neu
        }).ToList();

        float current = items.Sum(x => x.weight);

        // join to get defKey/bonus for equip
        var equip = inv.Equipment.Select(e =>
        {
            string key = "";
            float bonus = 0f;
            if (e.ItemDefinitionId != null)
            {
                var d = db.ItemDefinitions.FirstOrDefault(d => d.Id == e.ItemDefinitionId);
                if (d != null) { key = d.Key; bonus = d.CarryBonusKg; }
            }
            return new EquipDto { slot = e.Slot, defKey = key, carryBonusKg = bonus };
        }).ToList();

        float carryBonus = inv.Equipment
            .Where(e => e.ItemDefinitionId != null && e.Slot.Equals("backpack", StringComparison.OrdinalIgnoreCase))
            .Join(db.ItemDefinitions, e => e.ItemDefinitionId, d => d.Id, (e, d) => d.CarryBonusKg)
            .Sum();

        return new InventorySnapshotDto
        {
            invId = inv.Id,
            ownerType = inv.OwnerType.ToString().ToLowerInvariant(),
            ownerId = inv.OwnerId,
            w = inv.GridW,
            h = inv.GridH,
            capacityKg = inv.CapacityKg + carryBonus,
            currentKg = current,
            items = items,
            equip = equip
        };
    }

    #region Dev helpers

    // Seed a few item definitions if they do not exist yet
    [Command("inv_seed")]
    public void CmdInvSeed(Player p)
    {
        Task.Run(async () =>
        {
            var dbf = RequireDbf();
            await using var db = await dbf.CreateDbContextAsync();

            async Task EnsureDef(string key, string name, string category, float w, int max, string equipSlot = null, float carryBonus = 0f)
            {
                var d = await db.ItemDefinitions.FirstOrDefaultAsync(x => x.Key == key);
                if (d != null) return;

                db.ItemDefinitions.Add(new ItemDefinition
                {
                    Key = key,
                    Name = name,
                    Category = category,
                    WeightPerUnit = w,
                    MaxStack = max,
                    EquipSlot = (equipSlot ?? string.Empty).Trim(), // << wichtig
                    CarryBonusKg = carryBonus
                });
            }


            await EnsureDef("medikit", "Medikit", "consumable", 0.5f, 5);
            await EnsureDef("water", "Wasserflasche", "consumable", 0.5f, 10);
            await EnsureDef("burger", "Burger", "consumable", 0.6f, 10);
            // Example backpack for carry testing:
            await EnsureDef("bag_small", "Rucksack (klein)", "backpack", 0.8f, 1, equipSlot: "backpack", carryBonus: 10f);

            await db.SaveChangesAsync();

            NAPI.Task.Run(() =>
            {
                if (p?.Exists == true)
                    NAPI.Chat.SendChatMessageToPlayer(p, "~g~ItemDefinitions seeded (medikit, water, burger, bag_small).");
            });
        });
    }

    // Give N units of a definition to your player inventory
    [Command("gi")]
    public void CmdGiveItem(Player p, string defKey, int qty = 1)
    {
        Task.Run(async () =>
        {
            var dbf = RequireDbf();
            await using var db = await dbf.CreateDbContextAsync();

            var def = await db.ItemDefinitions.FirstOrDefaultAsync(d => d.Key == defKey);
            if (def == null)
            {
                NAPI.Task.Run(() =>
                {
                    if (p?.Exists == true)
                        NAPI.Chat.SendChatMessageToPlayer(p, $"~r~ItemDef '{defKey}' not found. Use /inv_seed first.");
                });
                return;
            }

            var inv = await EnsurePlayerInventoryAsync(db, p);
            await AddToInventoryAsync(db, inv, def.Id, qty);
            await db.SaveChangesAsync();

            // push UI update
            var snap = await BuildSnapshotAsync(db, inv);
            var json = JsonSerializer.Serialize(snap, InvJson);
            NAPI.Task.Run(() => { if (p?.Exists == true) p.TriggerEvent("client:inv:update", json); });
        });
    }

    // Fill your inventory with many items to test scroll
    [Command("inv_fill")]
    public void CmdInvFill(Player p)
    {
        Task.Run(async () =>
        {
            var dbf = RequireDbf();
            await using var db = await dbf.CreateDbContextAsync();

            var inv = await EnsurePlayerInventoryAsync(db, p);
            var defs = await db.ItemDefinitions.Take(6).ToListAsync();
            if (defs.Count == 0)
            {
                NAPI.Task.Run(() => NAPI.Chat.SendChatMessageToPlayer(p, "~y~/inv_seed first."));
                return;
            }

            foreach (var d in defs)
                await AddToInventoryAsync(db, inv, d.Id, d.MaxStack * 2); // overflow across multiple stacks

            await db.SaveChangesAsync();

            var snap = await BuildSnapshotAsync(db, inv);
            var json = JsonSerializer.Serialize(snap, InvJson);
            NAPI.Task.Run(() => { if (p?.Exists == true) p.TriggerEvent("client:inv:update", json); });
        });
    }

    // Stack-first add; creates new stacks in first free cells
    // All comments in English as requested

    private (int x, int y)? NextFreeCell(Inventory inv, HashSet<(int x, int y)> occ)
    {
        for (int y = 0; y < inv.GridH; y++)
            for (int x = 0; x < inv.GridW; x++)
                if (!occ.Contains((x, y))) return (x, y);
        return null;
    }

    private async Task AddToInventoryAsync(SpiritDbContext db, Inventory inv, int defId, int qty)
    {
        if (qty <= 0) return;

        // make sure relations are loaded once
        await db.Entry(inv).Collection(i => i.Items).LoadAsync();

        var def = await db.ItemDefinitions.FirstAsync(d => d.Id == defId);

        // build occupancy set from current items
        var occ = new HashSet<(int x, int y)>(inv.Items.Select(i => (i.SlotX, i.SlotY)));

        // fill partial stacks first (in-memory list so future loops see updated quantities)
        var stacks = inv.Items
            .Where(i => i.ItemDefinitionId == defId)
            .OrderBy(i => i.SlotY).ThenBy(i => i.SlotX)
            .ToList();

        foreach (var s in stacks)
        {
            if (qty <= 0) break;
            var can = def.MaxStack - s.Quantity;
            if (can <= 0) continue;
            var add = Math.Min(can, qty);
            s.Quantity += add;
            qty -= add;
        }

        // create new stacks using RAM occupancy
        while (qty > 0)
        {
            var pos = NextFreeCell(inv, occ);
            if (pos == null) break; // no space

            var add = Math.Min(def.MaxStack, qty);
            var ni = new InventoryItem
            {
                InventoryId = inv.Id,
                ItemDefinitionId = defId,
                SlotX = pos.Value.x,
                SlotY = pos.Value.y,
                Quantity = add
            };

            db.InventoryItems.Add(ni);
            inv.Items.Add(ni);             // keep the in-memory set in sync
            occ.Add((pos.Value.x, pos.Value.y));

            qty -= add;
        }
    }


    #endregion

}
