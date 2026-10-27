using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace KinshipPlugin;

/// <summary>The Items tab: a grid inventory, tooltips, creating items, giving them away, and using them.</summary>
public sealed class ItemsTab
{
    private static readonly Vector4 Gold = new(0.92f, 0.78f, 0.35f, 1f);
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.7f, 1f);
    private static readonly Vector4 Red = new(0.95f, 0.4f, 0.35f, 1f);
    private static readonly Vector4 Green = new(0.4f, 0.8f, 0.5f, 1f);
    private const float Slot = 52f;
    private const int Columns = 5;

    private enum Mode { Inventory, Form, Gift }
    private Mode mode = Mode.Inventory;

    // sign in
    private string name = Plugin.Config.Me().ItemsName.Length > 0 ? Plugin.Config.Me().ItemsName : Plugin.Config.Me().CombatName;
    private string memberPassword = "", recoveryCode = "";
    private bool joinExisting = true;

    // item form
    private ItemForm form = new();
    private string? editingId;

    // actions
    private ItemView? giving, sealing;
    private string giveTo = "";
    private string? confirmDelete;
    private int moveQty = 1;   // how many of a stack to give or delete

    // icon picker
    private readonly StashesTab stashTab = new();
    private readonly SignsTab signTab = new();
    private readonly BankTab bankTab = new();
    private readonly MailTab mailTab = new();
    private readonly ArmoryTab armoryTab = new();
    private readonly ProfileTab profileTab = new();
    private readonly DuelTab duelTab = new();
    private readonly StatusTab statusTab = new();
    private readonly QuestsTab questsTab = new();
    private string? confirmAdminDelete, confirmAdminRemove, confirmAdminStash, confirmAdminSign, confirmAdminMail;
    private string giftRecipient = "", giftStashId = "", giftStashName = "";
    private int giftCopies = 1;
    private int createQty = 1;
    private readonly Stack<ItemForm> parents = new();   // when editing what a combination makes, the form we came from
    private readonly IconPicker picker = new("pickicon");

    private readonly CombatTab combat;
    private readonly RpTab rp;
    public ItemsTab(CombatTab combat, RpTab rp) { this.combat = combat; this.rp = rp; }

    private ulong seenCharacter = ulong.MaxValue;

    public void Draw()
    {
        if (seenCharacter != Plugin.CharacterId)
        {
            seenCharacter = Plugin.CharacterId;
            name = Plugin.Config.Me().ItemsName.Length > 0 ? Plugin.Config.Me().ItemsName : Plugin.Config.Me().CombatName;
            if (name.Length == 0) name = Plugin.CharacterName();
            memberPassword = ""; recoveryCode = Plugin.Config.KnownRecoveryCode(); joinExisting = true; Plugin.Items.ClearFound(); mode = Mode.Inventory; editingId = null; giving = null; sealing = null; confirmDelete = null;
        }
        var s = Plugin.Items.Snapshot();
        if (mode == Mode.Gift) { DrawForm(s); return; }   // SMs can gift without being signed in as a member
        if (!s.SignedIn) { DrawSignIn(s); return; }
        Plugin.Items.RefreshIfDue();
        if (mode == Mode.Form || mode == Mode.Gift) { DrawForm(s); return; }
        if (ImGui.BeginTabBar("itemsub"))
        {
            if (ImGui.BeginTabItem("Inventory")) { DrawInventory(s); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Armory")) { armoryTab.Draw(s); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Profile")) { profileTab.Draw(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Stashes")) { stashTab.Draw(s); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Signs")) { signTab.Draw(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Status")) { statusTab.Draw(s); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Bank")) { bankTab.Draw(s); ImGui.EndTabItem(); }
            var unread = Plugin.Mail.Unread;
            if (ImGui.BeginTabItem((unread > 0 ? $"Mail ({unread})" : "Mail") + "###mailtab")) { mailTab.Draw(s); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Quests")) { questsTab.Draw(s); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Fight")) { duelTab.Draw(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Combat")) { combat.Draw(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Scenes")) { rp.Draw(); ImGui.EndTabItem(); }
            ImGui.EndTabBar();
        }
    }

    // ---------- signing in ----------
    private void DrawSignIn(ItemsSnap s)
    {
        ImGui.TextColored(Gold, "Welcome to the Kinship RP system");
        var who = Plugin.CharacterName();
        if (who.Length > 0) ImGui.TextColored(Dim, "Signing in as your character: " + who);
        if (Plugin.Config.HasLegacy())
        {
            ImGui.Spacing();
            var old = Plugin.Config.ItemsName.Length > 0 ? Plugin.Config.ItemsName : "a saved sign-in";
            ImGui.TextWrapped($"Your plugin has {old} saved from before each character got their own. Is that this character?");
            if (ImGui.Button("Yes, use it for " + (who.Length > 0 ? who : "this character") + "##claim")) { Plugin.Config.ClaimLegacy(); Plugin.ResetStates(); }
            ImGui.Spacing(); ImGui.Separator();
        }
        ImGui.Spacing();
        ImGui.TextWrapped("This is where everything personal to your character lives: your inventory, stashes, signs, statuses, bank, quests, and the combat and scene tools. It only exists inside this plugin and never touches the game.");
        ImGui.Spacing();
        ImGui.TextColored(Gold, "How it works");
        ImGui.TextWrapped("1. Register once below with your character's name and the member password. Ask your FC leader for the password.");
        ImGui.TextWrapped("2. You'll get one recovery code that covers all of your characters. Write it down somewhere safe. It's the only way back in if you move to a new computer or reinstall.");
        ImGui.TextWrapped("3. After that you stay signed in on this computer. Each of your characters has their own sign-in, so alts never share items, gold or quests. Register each alt the same way and it joins your recovery code.");
        ImGui.TextWrapped("4. Every time you open this tab you'll go straight to your own tabs.");
        ImGui.TextColored(Dim, "Only visiting? Use the Guest tab to join an encounter or a scene with just an invite link.");
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        ImGui.TextColored(Gold, "Register");
        ImGui.SetNextItemWidth(240f); ImGui.InputText("Your name##it", ref name, 40);
        ImGui.SetNextItemWidth(240f); ImGui.InputText("Member password##it", ref memberPassword, 100, ImGuiInputTextFlags.Password);
        ImGui.TextColored(Dim, "Ask your FC leader for the member password.");
        if (s.Error.Length > 0) ImGui.TextColored(Red, s.Error);
        if (s.Busy) ImGui.TextColored(Dim, "Working...");
        var known = Plugin.Config.KnownRecoveryCode();
        if (known.Length > 0) ImGui.Checkbox("This is another of my characters (use my existing recovery code)", ref joinExisting);
        if (s.Busy) { }
        else if (ImGui.Button("Register")) _ = Plugin.Items.RegisterAsync(name, memberPassword, known.Length > 0 && joinExisting ? known : "");

        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        ImGui.TextColored(Gold, "Already registered?");
        ImGui.TextWrapped("On a new computer, or for another of your characters that you registered before, enter your recovery code and pick which character this is.");
        ImGui.SetNextItemWidth(240f); ImGui.InputText("Recovery code##rc", ref recoveryCode, 40);
        if (!s.Busy && ImGui.Button("Find my characters")) _ = Plugin.Items.FindCharactersAsync(recoveryCode);
        var foundNames = Plugin.Items.Found();
        if (foundNames.Count > 0)
        {
            ImGui.TextColored(Gold, "Which one is " + (who.Length > 0 ? who : "this character") + "?");
            for (var i = 0; i < foundNames.Count; i++)
                if (!s.Busy && ImGui.Button(foundNames[i] + "##rec" + i)) _ = Plugin.Items.RecoverAsync(foundNames[i], recoveryCode);
        }

    }

    // ---------- the inventory ----------
    private void DrawInventory(ItemsSnap s)
    {
        ImGui.TextColored(Gold, s.Name + "'s items");
        ImGui.SameLine(); ImGui.TextColored(Dim, $"{s.Items.Count} / {s.Capacity}");
        ImGui.SameLine(); ImGui.TextColored(new Vector4(0.95f, 0.8f, 0.3f, 1f), $"Gold: {Plugin.Items.Gold:N0}");
        ImGui.SameLine(); if (ImGui.SmallButton("Refresh##items")) _ = Plugin.Items.RefreshAsync();
        if (s.Error.Length > 0) ImGui.TextColored(Red, s.Error);
        else if (s.Notice.Length > 0) ImGui.TextColored(Green, s.Notice);
        ImGui.Spacing();

        // the grid
        ItemView? hovered = null;
        for (var i = 0; i < s.Capacity; i++)
        {
            if (i % Columns != 0) ImGui.SameLine();
            if (i < s.Items.Count)
            {
                var it = s.Items[i];
                var wrap = ItemIcons.Wrap(it.Icon);
                var origin = ImGui.GetCursorScreenPos();
                if (wrap != null) ImGui.Image(wrap.Handle, new Vector2(Slot, Slot));
                else ImGui.Button("?##slot" + i, new Vector2(Slot, Slot));
                DrawQty(it, origin, Slot);
                if (ImGui.IsItemHovered()) { hovered = it; if (it.UseLabel.Length > 0 || it.CanCombine) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand); }
                if (ImGui.IsItemClicked(ImGuiMouseButton.Left) && !s.Busy)
                {
                    if (it.CanCombine) _ = Plugin.Items.CombineAsync(it.Id);
                    else if (it.UseLabel.Length > 0) _ = Plugin.Items.UseAsync(it.Id);
                }
                if (ImGui.BeginPopupContextItem("ctx" + it.Id))
                {
                    if (it.CanCombine && ImGui.MenuItem("Combine")) _ = Plugin.Items.CombineAsync(it.Id);
                    if (it.UseLabel.Length > 0 && ImGui.MenuItem(it.UseLabel)) _ = Plugin.Items.UseAsync(it.Id);
                    if (it.CanEdit && ImGui.MenuItem("Edit")) StartEdit(it);
                    if (it.CanEdit && it.UseLabel.Length > 0 && ImGui.MenuItem("Put an item inside...")) { sealing = it; giving = null; confirmDelete = null; }
                    if (it.CanEdit && it.Holds > 0 && ImGui.MenuItem("Take out what's inside")) _ = Plugin.Items.UnsealAsync(it.Id);
                    if (ImGui.MenuItem("Give to...")) { giving = it; giveTo = ""; moveQty = it.Qty; confirmDelete = null; _ = Plugin.Items.LoadMembersAsync(); }
                    if (ImGui.MenuItem("Delete")) { confirmDelete = it.Id; moveQty = it.Qty; giving = null; }
                    ImGui.EndPopup();
                }
            }
            else
            {
                ImGui.BeginDisabled(); ImGui.Button("##empty" + i, new Vector2(Slot, Slot)); ImGui.EndDisabled();
            }
        }
        if (hovered != null) DrawTooltip(hovered);

        ImGui.Spacing();
        ImGui.TextColored(Dim, "Click an item to use it. Right-click for more.");

        if (confirmDelete != null)
        {
            var it = s.Items.FirstOrDefault(x => x.Id == confirmDelete);
            if (it == null) confirmDelete = null;
            else
            {
                ImGui.Spacing(); ImGui.TextColored(Red, it.Qty > 1 ? $"Delete how many {it.Name}? (you have {it.Qty})" : $"Delete {it.Name} for good?");
                if (it.Qty > 1) { ImGui.SetNextItemWidth(100f); ImGui.InputInt("##delqty", ref moveQty, 1); moveQty = Math.Clamp(moveQty, 1, it.Qty); }
                else moveQty = 1;
                if (ImGui.Button(moveQty > 1 ? $"Yes, delete {moveQty}" : "Yes, delete")) { _ = Plugin.Items.DeleteAsync(it.Id, moveQty); confirmDelete = null; }
                ImGui.SameLine(); if (ImGui.Button("No##del")) confirmDelete = null;
            }
        }
        if (giving != null) DrawGive(s);
        if (sealing != null) DrawSeal(s);

        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        var full = s.Items.Count >= s.Capacity;
        if (full) ImGui.BeginDisabled();
        if (ImGui.Button("Create item")) StartCreate();
        if (full) { ImGui.EndDisabled(); ImGui.SameLine(); ImGui.TextColored(Dim, "Your inventory is full."); }

        ImGui.Spacing();
        if (ImGui.CollapsingHeader("My recovery code"))
        {
            ImGui.TextWrapped("This one code covers all of your characters. Write it down. If you play on another computer or reinstall, enter it on the RP tab and pick your character to get everything back.");
            ImGui.TextColored(Gold, s.Recovery.Length > 0 ? s.Recovery : "(not saved on this computer)");
            if (ImGui.SmallButton("Sign out")) { Plugin.Items.SignOut(); mode = Mode.Inventory; }
        }

    }

    private void DrawGive(ItemsSnap s)
    {
        if (giving == null) return;
        ImGui.Spacing(); ImGui.TextColored(Gold, $"Give {giving.Name} to:");
        if (giving.Qty > 1) { ImGui.SetNextItemWidth(100f); ImGui.InputInt($"How many (you have {giving.Qty})##giveqty", ref moveQty, 1); moveQty = Math.Clamp(moveQty, 1, giving.Qty); } else moveQty = 1;
        if (s.Members.Count == 0) ImGui.TextColored(Dim, "Nobody else has joined the RP system yet.");
        else
        {
            ImGui.SetNextItemWidth(220f);
            if (ImGui.BeginCombo("##giveto", giveTo.Length > 0 ? giveTo : "Choose a member"))
            {
                foreach (var m in s.Members) if (ImGui.Selectable(m, m == giveTo)) giveTo = m;
                ImGui.EndCombo();
            }
        }
        if (giveTo.Length == 0) ImGui.BeginDisabled();
        if (ImGui.Button(moveQty > 1 ? $"Give {moveQty}" : "Give")) { _ = Plugin.Items.GiveAsync(giving.Id, giveTo, moveQty); giving = null; }
        if (giveTo.Length == 0) ImGui.EndDisabled();
        ImGui.SameLine(); if (ImGui.Button("Cancel##give")) giving = null;
    }

    private void DrawSeal(ItemsSnap s)
    {
        if (sealing == null) return;
        ImGui.Spacing(); ImGui.TextColored(Gold, $"Put an item inside {sealing.Name}:");
        ImGui.TextColored(Dim, "Up to 3. They come out when it is used.");
        var others = s.Items.Where(x => x.Id != sealing.Id).ToList();
        if (others.Count == 0) ImGui.TextColored(Dim, "You have nothing else to put inside.");
        foreach (var o in others)
        {
            ImGui.Text(o.Name); ImGui.SameLine();
            if (!s.Busy && ImGui.SmallButton("Put inside##sl" + o.Id)) { _ = Plugin.Items.SealAsync(sealing.Id, o.Id); }
        }
        if (ImGui.Button("Done##seal")) sealing = null;
    }

    /// <summary>Puts the stack size in the corner of an item slot. Call right after drawing the slot, with the cursor position taken just before it.</summary>
    internal static void DrawQty(ItemView it, Vector2 origin, float size)
    {
        if (it.Qty <= 1) return;
        var t = it.Qty.ToString();
        var w = ImGui.CalcTextSize(t);
        var at = new Vector2(origin.X + size - w.X - 3f, origin.Y + size - w.Y - 1f);
        ImGui.SetCursorScreenPos(at + new Vector2(1f, 1f)); ImGui.TextColored(new Vector4(0f, 0f, 0f, 1f), t);
        ImGui.SetCursorScreenPos(at); ImGui.TextColored(new Vector4(1f, 1f, 1f, 1f), t);
        ImGui.SetCursorScreenPos(origin); ImGui.Dummy(new Vector2(size, size));   // gives the slot back as the last item, so hover, click and layout work as before
    }

    internal static void DrawTooltip(ItemView it)
    {
        ImGui.BeginTooltip();
        var wrap = ItemIcons.Wrap(it.Icon);
        if (wrap != null) { ImGui.Image(wrap.Handle, new Vector2(40f, 40f)); ImGui.SameLine(); }
        ImGui.TextColored(Gold, it.Name);
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 22f);
        if (it.Description.Length > 0) { ImGui.Spacing(); ImGui.TextUnformatted(it.Description); }
        if (it.Flavor.Length > 0) { ImGui.Spacing(); ImGui.TextColored(Dim, "“" + it.Flavor + "”"); }
        if (it.Requires.Length > 0) { ImGui.Spacing(); ImGui.TextColored(new Vector4(0.95f, 0.65f, 0.35f, 1f), "Needs: " + it.Requires + (it.RequiresConsume ? "  (used up)" : "")); }
        if (it.WearSlot.Length > 0)
        {
            ImGui.Spacing(); ImGui.TextColored(Gold, "Wearable: " + ArmoryState.SlotName(it.WearSlot));
            if (it.GearAtk != 0) ImGui.TextColored(Green, (it.GearAtk > 0 ? "+" : "") + it.GearAtk + " to attack rolls");
            if (it.GearHeal != 0) ImGui.TextColored(Green, (it.GearHeal > 0 ? "+" : "") + it.GearHeal + " to heal rolls");
            if (it.LifespanDays > 0)
            {
                var left = DateTime.TryParse(it.BreaksAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var t) ? (t - DateTime.UtcNow) : (TimeSpan?)null;
                ImGui.TextColored(Dim, left == null ? $"Breaks {it.LifespanDays} days after it is first worn." : left.Value.TotalHours < 24 ? "Breaks in less than a day." : $"Breaks in about {Math.Ceiling(left.Value.TotalDays)} days.");
            }
        }
        if (it.Qty > 1) { ImGui.Spacing(); ImGui.TextColored(Dim, $"You have {it.Qty}."); }
        if (it.Holds > 0) { ImGui.Spacing(); ImGui.TextColored(Gold, it.Holds == 1 ? "Something is inside." : $"{it.Holds} things are inside."); }
        if (it.CanCombine) { ImGui.Spacing(); ImGui.TextColored(Green, "Click to combine with other reagents"); }
        if (it.UseLabel.Length > 0) { ImGui.Spacing(); ImGui.TextColored(Green, "Click to " + it.UseLabel.ToLowerInvariant() + (it.Consumable ? "  (used up)" : "")); }
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
    }

    // ---------- creating and editing ----------
    private static void DrawGearFields(ItemForm form, bool gift)
    {
        if (!ImGui.CollapsingHeader("Wearable gear (optional)##gear")) return;
        ImGui.TextColored(Dim, "Make this something people can wear in the Armory.");
        var labels = new[] { ("", "Not wearable"), ("helm", "Head"), ("chest", "Chest"), ("cloak", "Cloak"), ("legs", "Legs"), ("feet", "Feet"), ("ring", "Ring"), ("necklace", "Necklace"), ("weapon", "Weapon") };
        ImGui.SetNextItemWidth(200f);
        if (ImGui.BeginCombo("Worn on##gearslot", labels.First(l => l.Item1 == form.WearSlot).Item2))
        {
            foreach (var (key, label) in labels) if (ImGui.Selectable(label, key == form.WearSlot)) form.WearSlot = key;
            ImGui.EndCombo();
        }
        if (form.WearSlot.Length == 0) return;
        ImGui.SetNextItemWidth(100f); ImGui.InputInt("Breaks after (days)##gearlife", ref form.LifespanDays, 1);
        form.LifespanDays = Math.Clamp(form.LifespanDays, 0, 365);
        ImGui.TextColored(Dim, "0 means it never breaks. The countdown starts the first time it is worn. Broken gear turns into a useless \"Broken\" item.");
        if (gift)
        {
            ImGui.SetNextItemWidth(100f); ImGui.InputInt("Attack roll bonus##gearatk", ref form.GearAtk, 1);
            ImGui.SetNextItemWidth(100f); ImGui.InputInt("Heal roll bonus##gearheal", ref form.GearHeal, 1);
            form.GearAtk = Math.Clamp(form.GearAtk, -5, 5); form.GearHeal = Math.Clamp(form.GearHeal, -5, 5);
            ImGui.TextColored(Dim, "-5 to +5 added to rolls while worn. All worn gear together is capped at +5. Only Advocate gifts can do this.");
        }
        else ImGui.TextColored(Dim, "Roll bonuses can only be added by Advocate gifts.");
    }

    private void StartCreate() { createQty = 1; parents.Clear(); form = new ItemForm(); editingId = null; mode = Mode.Form; Plugin.Items.ClearMessages(); }
    private static ItemForm FormFrom(ItemView it, bool recipe = true)
    {
        var f = new ItemForm
        {
            Name = it.Name, Icon = it.Icon, Description = it.Description, Flavor = it.Flavor, UseLabel = it.UseLabel, UseText = it.UseText ?? "", Consumable = it.Consumable,
            Requires = it.Requires, RequiresConsume = it.RequiresConsume, StName = it.StName, StDesc = it.StDesc, StIcon = it.StIcon, StDebuff = it.StDebuff, WearSlot = it.WearSlot, LifespanDays = it.LifespanDays, GearAtk = it.GearAtk, GearHeal = it.GearHeal, StMinutes = it.StMinutes, StAtk = it.StAtk, StHeal = it.StHeal, Cures = it.Cures, StType = it.StType, StCure = it.StCure,
        };
        if (recipe && it.CombineWith != null)
        {
            for (var i = 0; i < 3 && i < it.CombineWith.Count; i++) f.With[i] = it.CombineWith[i];
            f.FailChance = it.CombineFail;
            if (it.Result != null) f.Result = FormFrom(it.Result, false);
        }
        return f;
    }
    private void StartEdit(ItemView it)
    {
        parents.Clear();
        form = FormFrom(it);
        editingId = it.Id; mode = Mode.Form; Plugin.Items.ClearMessages();
    }

    private void DrawForm(ItemsSnap s)
    {
        var gift = mode == Mode.Gift;
        var inResult = parents.Count > 0;
        ImGui.TextColored(Gold, inResult ? "What the combination makes" : gift ? "Gift an item (Advocate)" : editingId == null ? "Create an item" : "Edit item");
        if (inResult) ImGui.TextColored(Dim, "Fill this in like any other item. It is made when the reagents are combined.");
        ImGui.Spacing();
        if (gift && !inResult)
        {
            var dm = Plugin.Items.AdminSnapshot();
            ImGui.SetNextItemWidth(220f);
            if (ImGui.BeginCombo("Give to##gift", giftRecipient.Length > 0 ? giftRecipient : "Choose a member"))
            {
                foreach (var m in dm.Members) if (ImGui.Selectable(m, m == giftRecipient)) { giftRecipient = m; giftStashId = ""; giftStashName = ""; }
                ImGui.EndCombo();
            }
            Plugin.Stashes.RefreshIfDue(30);
            ImGui.SetNextItemWidth(220f);
            if (ImGui.BeginCombo("Or put it in a stash##giftst", giftStashId.Length > 0 ? giftStashName : "Choose a stash"))
            {
                foreach (var st in Plugin.Stashes.All) if (ImGui.Selectable($"{st.Name}  ({st.Zone})##gs{st.Id}", st.Id == giftStashId)) { giftStashId = st.Id; giftStashName = st.Name; giftRecipient = ""; }
                ImGui.EndCombo();
            }
            ImGui.SetNextItemWidth(100f); ImGui.InputInt("Copies##giftn", ref giftCopies, 1);
            if (giftCopies < 1) giftCopies = 1;
            if (giftCopies > 100) giftCopies = 100;
            ImGui.TextColored(Dim, "Up to 10 identical copies at once, like several herbs for a stash.");
        }

        var wrap = form.Icon.Length > 0 ? ItemIcons.Wrap(form.Icon) : null;
        if (wrap != null) ImGui.Image(wrap.Handle, new Vector2(Slot, Slot)); else ImGui.Button("?##formicon", new Vector2(Slot, Slot));
        ImGui.SameLine();
        if (ImGui.Button("Choose icon")) picker.Open(v => form.Icon = v);
        picker.Draw();

        ImGui.SetNextItemWidth(260f); ImGui.InputText("Name##f", ref form.Name, 40);
        ImGui.Text("Description"); ImGui.InputTextMultiline("##fdesc", ref form.Description, 300, new Vector2(ImGui.GetContentRegionAvail().X - 8f, 56f));
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 8f); ImGui.InputText("##fflavor", ref form.Flavor, 150);
        ImGui.TextColored(Dim, "Flavor text, shown in quotes.");
        if (!gift && !inResult && editingId == null)
        {
            ImGui.SetNextItemWidth(100f); ImGui.InputInt("Quantity##createn", ref createQty, 1);
            if (createQty < 1) createQty = 1;
            if (createQty > 100) createQty = 100;
            ImGui.SameLine(); ImGui.TextColored(Dim, "How many to make. 10 fit in one slot.");
        }

        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        ImGui.TextColored(Gold, "On use (optional)");
        ImGui.SetNextItemWidth(160f); ImGui.InputText("Button word##fl", ref form.UseLabel, 20);
        ImGui.TextColored(Dim, "e.g. Eat, Read, Open. Leave blank for an item that can't be used.");
        ImGui.Text("What happens"); ImGui.InputTextMultiline("##fuse", ref form.UseText, 400, new Vector2(ImGui.GetContentRegionAvail().X - 8f, 64f));
        ImGui.Checkbox("Used up when used", ref form.Consumable);
        ImGui.Spacing();
        DrawGearFields(form, gift);
        ImGui.Spacing();
        ImGui.SetNextItemWidth(260f); ImGui.InputText("Needs this item##freq", ref form.Requires, 40);
        ImGui.TextColored(Dim, "Optional. The exact name of another item, like The Harbinger's Key. It must be in the user's inventory to use this.");
        if (form.Requires.Trim().Length > 0) ImGui.Checkbox("That item is used up too", ref form.RequiresConsume);
        if (form.Requires.Trim().Length > 0 && form.UseText.Trim().Length == 0) ImGui.TextColored(Red, "Fill in \"What happens\" so there is an action, like Open.");
        ImGui.TextColored(Dim, "To make something hold other items, save this item first, then right-click it and choose Put an item inside.");

        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        ImGui.TextColored(Gold, "Status (optional)");
        if (gift && !inResult)
        {
            var qs = Plugin.Quests.Snapshot().Admin;
            ImGui.SetNextItemWidth(240f);
            if (ImGui.BeginCombo("Starts a quest##fsq", form.StartsQuest.Length > 0 ? qs.FirstOrDefault(x => x.Id == form.StartsQuest)?.Title ?? "(quest)" : "No quest"))
            {
                if (ImGui.Selectable("No quest", form.StartsQuest.Length == 0)) form.StartsQuest = "";
                foreach (var q in qs) if (ImGui.Selectable(q.Title + "##fsq" + q.Id, q.Id == form.StartsQuest)) form.StartsQuest = q.Id;
                ImGui.EndCombo();
            }
            ImGui.TextColored(Dim, qs.Count == 0 ? "Open the SM Tools tab, Quests, and press Load quests to see your quests here." : "Using the item offers the quest with Accept and Decline. Needs an action below, like Read.");
        }
        ImGui.SetNextItemWidth(240f); ImGui.InputText("Gives the status##fst", ref form.StName, 30);
        ImGui.TextColored(Dim, "A buff or debuff the person gets when they use this, like Chocolate poison.");
        if (form.StName.Trim().Length > 0)
        {
            ImGui.Indent();
            var swrap = ItemIcons.Wrap(form.StIcon.Length > 0 ? form.StIcon : form.Icon);
            if (swrap != null) { ImGui.Image(swrap.Handle, new Vector2(32f, 32f)); ImGui.SameLine(); }
            if (ImGui.SmallButton("Choose status icon")) picker.Open(v => form.StIcon = v);
            if (form.StIcon.Length > 0) { ImGui.SameLine(); if (ImGui.SmallButton("Use the item's icon")) form.StIcon = ""; }
            ImGui.Text("What it does"); ImGui.InputTextMultiline("##fstd", ref form.StDesc, 200, new Vector2(ImGui.GetContentRegionAvail().X - 8f, 52f));
            ImGui.SetNextItemWidth(120f); ImGui.InputInt("Minutes##fstm", ref form.StMinutes, 1);
            if (form.StMinutes < 0) form.StMinutes = 0;
            ImGui.TextColored(Dim, "0 means it lasts until something removes it.");
            ImGui.Checkbox("It is a debuff (bad)", ref form.StDebuff);
            if (gift)
            {
                ImGui.SetNextItemWidth(100f); ImGui.InputInt("Attack roll bonus##fsta", ref form.StAtk, 1);
                ImGui.SetNextItemWidth(100f); ImGui.InputInt("Heal roll bonus##fsth", ref form.StHeal, 1);
                form.StAtk = Math.Clamp(form.StAtk, -5, 5); form.StHeal = Math.Clamp(form.StHeal, -5, 5);
                ImGui.TextColored(Dim, "-5 to +5 added to rolls in quest fights and encounters while the status lasts. Only Advocate gifts can do this.");
            }
            ImGui.SetNextItemWidth(160f);
            if (ImGui.BeginCombo("Kind##fstt", form.StType == "sickness" ? "Sickness" : form.StType == "curse" ? "Curse" : "Neither"))
            {
                if (ImGui.Selectable("Neither", form.StType.Length == 0)) form.StType = "";
                if (ImGui.Selectable("Sickness (Life Watch can diagnose)", form.StType == "sickness")) form.StType = "sickness";
                if (ImGui.Selectable("Curse (Onyx Blood Guard can diagnose)", form.StType == "curse")) form.StType = "curse";
                ImGui.EndCombo();
            }
            if (form.StType.Length > 0)
            {
                ImGui.Text("Cure information (shown to a successful diagnosis)"); ImGui.InputTextMultiline("##fstc", ref form.StCure, 200, new Vector2(ImGui.GetContentRegionAvail().X - 8f, 52f));
            }
            ImGui.Unindent();
        }
        ImGui.SetNextItemWidth(240f); ImGui.InputText("Removes the status##fcu", ref form.Cures, 30);
        ImGui.TextColored(Dim, "The exact name of a status this cures, like Chocolate poison. If they don't have it, nothing is used up.");
        if ((form.StName.Trim().Length > 0 || form.Cures.Trim().Length > 0) && form.UseText.Trim().Length == 0) ImGui.TextColored(Red, "Fill in \"What happens\" so there is an action, like Drink.");

        if (!inResult)
        {
            ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
            ImGui.TextColored(Gold, "Combining (optional)");
            ImGui.TextColored(Dim, "Makes this a reagent. Click it to combine it with up to 3 other items and make something new. All of them are used up (and also if it fails, if you allow that).");
            for (var i = 0; i < 3; i++) { ImGui.SetNextItemWidth(240f); ImGui.InputText($"Needs this item##with{i}", ref form.With[i], 40); }
            if (form.With.Any(w => w.Trim().Length > 0))
            {
                if (form.Result == null || form.Result.Name.Trim().Length == 0) ImGui.TextColored(Dim, "Nothing set up to make yet.");
                else ImGui.TextColored(Green, "Makes: " + form.Result.Name);
                if (ImGui.Button("Set up what it makes...")) { form.Result ??= new ItemForm(); parents.Push(form); form = form.Result; }
                var canFail = form.FailChance > 0;
                if (ImGui.Checkbox("This combination can fail", ref canFail)) form.FailChance = canFail ? 25 : 0;
                if (form.FailChance > 0)
                {
                    ImGui.SetNextItemWidth(100f); ImGui.InputInt("Chance of failing (%)##failchance", ref form.FailChance, 5);
                    form.FailChance = Math.Clamp(form.FailChance, 1, 99);
                    ImGui.TextColored(Dim, "If it fails, every item put in is used up and nothing is made.");
                }
                else ImGui.TextColored(Dim, "Left unticked, it always works.");
            }
        }

        var adm = Plugin.Items.AdminSnapshot();
        if (gift && adm.Error.Length > 0) ImGui.TextColored(Red, adm.Error);
        else if (!gift && s.Error.Length > 0) ImGui.TextColored(Red, s.Error);
        ImGui.Spacing();
        if (inResult)
        {
            var done = form.Name.Trim().Length > 0 && form.Icon.Length > 0;
            if (!done) ImGui.BeginDisabled();
            if (ImGui.Button("Done##result")) form = parents.Pop();
            if (!done) ImGui.EndDisabled();
            if (form.Icon.Length == 0) { ImGui.SameLine(); ImGui.TextColored(Dim, "Choose an icon first."); }
            return;
        }
        var ok = form.Name.Trim().Length > 0 && form.Icon.Length > 0 && !s.Busy && (!gift || ((giftRecipient.Length > 0 || giftStashId.Length > 0) && !adm.Busy));
        if (!ok) ImGui.BeginDisabled();
        if (ImGui.Button(gift ? "Send gift" : editingId == null ? "Create item" : "Save changes")) _ = SubmitAsync();
        if (!ok) ImGui.EndDisabled();
        ImGui.SameLine(); if (ImGui.Button("Cancel##form")) { mode = Mode.Inventory; parents.Clear(); }
        if (form.Icon.Length == 0) { ImGui.SameLine(); ImGui.TextColored(Dim, "Choose an icon first."); }
    }

    private async System.Threading.Tasks.Task SubmitAsync()
    {
        var f = form; var id = editingId;
        if (mode == Mode.Gift) { if (await Plugin.Items.AdminGrantAsync(f, giftRecipient, giftStashId, giftCopies)) { mode = Mode.Inventory; await Plugin.Items.AdminLoadAsync(); } return; }
        var ok = id == null ? await Plugin.Items.CreateAsync(f, createQty) : await Plugin.Items.UpdateAsync(id, f);
        if (ok) { mode = Mode.Inventory; parents.Clear(); }
    }

    // ---------- Advocate tools (leader and officers) ----------
    /// <summary>The SM Tools tab: every Advocate tool in one place, whether or not you're signed in to the RP system.</summary>
    public void DrawSmTools()
    {
        var s = Plugin.Items.Snapshot();
        if (mode == Mode.Gift) { DrawForm(s); return; }
        ImGui.TextColored(Gold, "SM Tools");
        ImGui.TextWrapped("For the Harbinger, Advocates and anyone holding the bank password. Each section asks for its password. Without it, nothing here works.");
        ImGui.Spacing();
        DrawDmTools();
        questsTab.DrawTools(s);
        statusTab.DrawTools(s);
        bankTab.DrawTools();
        ReportsAdmin.Draw();
        ProfileAdmin.Draw();
        MarksAdmin.Draw();
    }

    private void DrawDmTools()
    {
        if (!ImGui.CollapsingHeader("Items, gifts, stashes, signs, mail and members")) return;
        ImGui.TextWrapped("For the Harbinger and the Advocates. Uses the creation password.");
        UiUtil.CreatePasswordInput();
        var d = Plugin.Items.AdminSnapshot();
        if (d.Error.Length > 0) ImGui.TextColored(Red, d.Error);
        else if (d.Notice.Length > 0) ImGui.TextColored(Green, d.Notice);
        if (d.Busy) ImGui.TextColored(Dim, "Working...");
        else if (ImGui.Button(d.Loaded ? "Reload lists" : "Load everyone's items")) _ = Plugin.Items.AdminLoadAsync();
        if (!d.Loaded) return;

        ImGui.SameLine();
        if (ImGui.Button("Gift an item...")) { form = new ItemForm(); editingId = null; giftRecipient = ""; giftStashId = ""; giftStashName = ""; giftCopies = 1; parents.Clear(); mode = Mode.Gift; Plugin.Items.ClearMessages(); }

        ImGui.Spacing();
        ImGui.TextColored(Gold, $"All items ({d.Items.Count})");
        if (d.Items.Count == 0) ImGui.TextColored(Dim, "No items yet.");
        foreach (var it in d.Items)
        {
            var wrap = ItemIcons.Wrap(it.Icon);
            if (wrap != null) { ImGui.Image(wrap.Handle, new Vector2(24f, 24f)); ImGui.SameLine(); }
            ImGui.Text(it.Name);
            if (ImGui.IsItemHovered() && it.Description.Length > 0) ImGui.SetTooltip(it.Description);
            ImGui.SameLine(); ImGui.TextColored(Dim, $"held by {it.Owner}, made by {it.Creator}");
            ImGui.SameLine();
            if (confirmAdminDelete == it.Id)
            {
                if (ImGui.SmallButton("Really delete##ad" + it.Id)) { _ = Plugin.Items.AdminDeleteItemAsync(it.Id); confirmAdminDelete = null; }
                ImGui.SameLine(); if (ImGui.SmallButton("No##ad" + it.Id)) confirmAdminDelete = null;
            }
            else if (ImGui.SmallButton("Delete##ad" + it.Id)) confirmAdminDelete = it.Id;
        }

        var stashList = Plugin.Items.AdminStashes();
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        ImGui.TextColored(Gold, $"Stashes ({stashList.Count})");
        foreach (var st in stashList)
        {
            ImGui.Text(st.Name);
            ImGui.SameLine(); ImGui.TextColored(Dim, $"{st.Zone}, by {st.Owner}, {st.Count} items" + (st.IsPrivate ? ", private" : ""));
            ImGui.SameLine();
            if (confirmAdminStash == st.Id)
            {
                if (ImGui.SmallButton("Really delete##as" + st.Id)) { _ = Plugin.Items.AdminDeleteStashAsync(st.Id); confirmAdminStash = null; }
                ImGui.SameLine(); if (ImGui.SmallButton("No##as" + st.Id)) confirmAdminStash = null;
            }
            else if (ImGui.SmallButton("Delete##as" + st.Id)) confirmAdminStash = st.Id;
        }

        var signList = Plugin.Items.AdminSigns();
        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        ImGui.TextColored(Gold, $"Signs ({signList.Count})");
        foreach (var sg in signList)
        {
            ImGui.Text(sg.Name);
            ImGui.SameLine(); ImGui.TextColored(Dim, $"{sg.Zone}, by {sg.Owner}" + (sg.Gold > 0 ? $", {sg.Gold:N0} gold" : ""));
            ImGui.SameLine();
            if (confirmAdminSign == sg.Id)
            {
                if (ImGui.SmallButton("Really delete##ag" + sg.Id)) { _ = Plugin.Items.AdminDeleteSignAsync(sg.Id); confirmAdminSign = null; }
                ImGui.SameLine(); if (ImGui.SmallButton("No##ag" + sg.Id)) confirmAdminSign = null;
            }
            else if (ImGui.SmallButton("Delete##ag" + sg.Id)) confirmAdminSign = sg.Id;
        }

        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        var mails = Plugin.Mail.AdminList();
        ImGui.TextColored(Gold, $"Mail ({mails.Count})");
        ImGui.SameLine(); if (ImGui.SmallButton("Load mail")) _ = Plugin.Mail.AdminLoadAsync();
        if (Plugin.Mail.AdminError().Length > 0) ImGui.TextColored(Red, Plugin.Mail.AdminError());
        foreach (var ml in mails)
        {
            ImGui.Text(ml.Subject);
            ImGui.SameLine(); ImGui.TextColored(Dim, $"{ml.From} to {ml.To}" + (ml.Anonymous ? " (sent as anonymous)" : "") + (ml.Returned ? " (returned)" : "") + (ml.Gold > 0 ? $", {ml.Gold:N0} gold" : ""));
            ImGui.SameLine();
            if (confirmAdminMail == ml.Id)
            {
                if (ImGui.SmallButton("Really delete##am" + ml.Id)) { _ = Plugin.Mail.AdminDeleteAsync(ml.Id); confirmAdminMail = null; }
                ImGui.SameLine(); if (ImGui.SmallButton("No##am" + ml.Id)) confirmAdminMail = null;
            }
            else if (ImGui.SmallButton("Delete##am" + ml.Id)) confirmAdminMail = ml.Id;
        }

        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        ImGui.TextColored(Gold, $"Members ({d.Members.Count})");
        foreach (var m in d.Members)
        {
            ImGui.Text(m); ImGui.SameLine();
            if (confirmAdminRemove == m)
            {
                ImGui.TextColored(Red, "Delete their items and sign them out?"); ImGui.SameLine();
                if (ImGui.SmallButton("Yes, remove##rm" + m)) { _ = Plugin.Items.AdminRemoveMemberAsync(m); confirmAdminRemove = null; }
                ImGui.SameLine(); if (ImGui.SmallButton("No##rm" + m)) confirmAdminRemove = null;
            }
            else if (ImGui.SmallButton("Remove##rm" + m)) confirmAdminRemove = m;
        }
    }
}
