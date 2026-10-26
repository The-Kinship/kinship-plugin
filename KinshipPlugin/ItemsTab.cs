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
    private string name = Plugin.Config.ItemsName.Length > 0 ? Plugin.Config.ItemsName : Plugin.Config.CombatName;
    private string memberPassword = "", recoveryName = "", recoveryCode = "";

    // item form
    private ItemForm form = new();
    private string? editingId;

    // actions
    private ItemView? giving, sealing;
    private string giveTo = "";
    private string? confirmDelete;

    // icon picker
    private readonly StashesTab stashTab = new();
    private readonly SignsTab signTab = new();
    private string? confirmAdminDelete, confirmAdminRemove, confirmAdminStash;
    private string giftRecipient = "";
    private int browseStart = 20000, goTo = 20000, iconNumber = 20001;
    private string category = "All";

    public void Draw()
    {
        var s = Plugin.Items.Snapshot();
        if (mode == Mode.Gift) { DrawForm(s); return; }   // DMs can gift without being signed in as a member
        if (!s.SignedIn) { DrawSignIn(s); return; }
        Plugin.Items.RefreshIfDue();
        if (mode == Mode.Form || mode == Mode.Gift) { DrawForm(s); return; }
        if (ImGui.BeginTabBar("itemsub"))
        {
            if (ImGui.BeginTabItem("Inventory")) { DrawInventory(s); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Stashes")) { stashTab.Draw(s); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Signs")) { signTab.Draw(); ImGui.EndTabItem(); }
            ImGui.EndTabBar();
        }
    }

    // ---------- signing in ----------
    private void DrawSignIn(ItemsSnap s)
    {
        ImGui.TextColored(Gold, "RP items");
        ImGui.TextWrapped("Make items for your character, keep them in your inventory, give them to others, and use them in RP. Items only exist in this plugin. They never touch the game.");
        ImGui.Spacing();
        ImGui.TextColored(Gold, "Join the item system");
        ImGui.SetNextItemWidth(240f); ImGui.InputText("Your name##it", ref name, 40);
        ImGui.SetNextItemWidth(240f); ImGui.InputText("Member password##it", ref memberPassword, 100, ImGuiInputTextFlags.Password);
        ImGui.TextColored(Dim, "Ask your FC leader for the member password.");
        if (s.Error.Length > 0) ImGui.TextColored(Red, s.Error);
        if (s.Busy) ImGui.TextColored(Dim, "Working...");
        else if (ImGui.Button("Join")) _ = Plugin.Items.RegisterAsync(name, memberPassword);

        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        ImGui.TextColored(Gold, "Already joined on another computer?");
        ImGui.SetNextItemWidth(240f); ImGui.InputText("Your name##rc", ref recoveryName, 40);
        ImGui.SetNextItemWidth(240f); ImGui.InputText("Recovery code##rc", ref recoveryCode, 40);
        if (!s.Busy && ImGui.Button("Get my items back")) _ = Plugin.Items.RecoverAsync(recoveryName, recoveryCode);

        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        DrawDmTools();
    }

    // ---------- the inventory ----------
    private void DrawInventory(ItemsSnap s)
    {
        ImGui.TextColored(Gold, s.Name + "'s items");
        ImGui.SameLine(); ImGui.TextColored(Dim, $"{s.Items.Count} / {s.Capacity}");
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
                if (wrap != null) ImGui.Image(wrap.Handle, new Vector2(Slot, Slot));
                else ImGui.Button("?##slot" + i, new Vector2(Slot, Slot));
                if (ImGui.IsItemHovered()) { hovered = it; if (it.UseLabel.Length > 0) ImGui.SetMouseCursor(ImGuiMouseCursor.Hand); }
                if (ImGui.IsItemClicked(ImGuiMouseButton.Left) && it.UseLabel.Length > 0 && !s.Busy) _ = Plugin.Items.UseAsync(it.Id);
                if (ImGui.BeginPopupContextItem("ctx" + it.Id))
                {
                    if (it.UseLabel.Length > 0 && ImGui.MenuItem(it.UseLabel)) _ = Plugin.Items.UseAsync(it.Id);
                    if (it.CanEdit && ImGui.MenuItem("Edit")) StartEdit(it);
                    if (it.CanEdit && it.UseLabel.Length > 0 && ImGui.MenuItem("Put an item inside...")) { sealing = it; giving = null; confirmDelete = null; }
                    if (it.CanEdit && it.Holds > 0 && ImGui.MenuItem("Take out what's inside")) _ = Plugin.Items.UnsealAsync(it.Id);
                    if (ImGui.MenuItem("Give to...")) { giving = it; giveTo = ""; confirmDelete = null; _ = Plugin.Items.LoadMembersAsync(); }
                    if (ImGui.MenuItem("Delete")) { confirmDelete = it.Id; giving = null; }
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
                ImGui.Spacing(); ImGui.TextColored(Red, $"Delete {it.Name} for good?");
                if (ImGui.Button("Yes, delete")) { _ = Plugin.Items.DeleteAsync(it.Id); confirmDelete = null; }
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
            ImGui.TextWrapped("Write this down. If you play on another computer or reinstall, enter your name and this code on the Items tab to get your items back.");
            ImGui.TextColored(Gold, s.Recovery.Length > 0 ? s.Recovery : "(not saved on this computer)");
            if (ImGui.SmallButton("Sign out of items")) { Plugin.Items.SignOut(); mode = Mode.Inventory; }
        }

        ImGui.Spacing();
        DrawDmTools();
    }

    private void DrawGive(ItemsSnap s)
    {
        if (giving == null) return;
        ImGui.Spacing(); ImGui.TextColored(Gold, $"Give {giving.Name} to:");
        if (s.Members.Count == 0) ImGui.TextColored(Dim, "Nobody else has joined the item system yet.");
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
        if (ImGui.Button("Give")) { _ = Plugin.Items.GiveAsync(giving.Id, giveTo); giving = null; }
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
        if (it.Holds > 0) { ImGui.Spacing(); ImGui.TextColored(Gold, it.Holds == 1 ? "Something is inside." : $"{it.Holds} things are inside."); }
        if (it.UseLabel.Length > 0) { ImGui.Spacing(); ImGui.TextColored(Green, "Click to " + it.UseLabel.ToLowerInvariant() + (it.Consumable ? "  (used up)" : "")); }
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
    }

    // ---------- creating and editing ----------
    private void StartCreate() { form = new ItemForm(); editingId = null; mode = Mode.Form; Plugin.Items.ClearMessages(); }
    private void StartEdit(ItemView it)
    {
        form = new ItemForm { Name = it.Name, Icon = it.Icon, Description = it.Description, Flavor = it.Flavor, UseLabel = it.UseLabel, UseText = it.UseText ?? "", Consumable = it.Consumable, Requires = it.Requires, RequiresConsume = it.RequiresConsume };
        editingId = it.Id; mode = Mode.Form; Plugin.Items.ClearMessages();
    }

    private void DrawForm(ItemsSnap s)
    {
        var gift = mode == Mode.Gift;
        ImGui.TextColored(Gold, gift ? "Gift an item (Advocate)" : editingId == null ? "Create an item" : "Edit item");
        ImGui.Spacing();
        if (gift)
        {
            var dm = Plugin.Items.AdminSnapshot();
            ImGui.SetNextItemWidth(220f);
            if (ImGui.BeginCombo("Give to##gift", giftRecipient.Length > 0 ? giftRecipient : "Choose a member"))
            {
                foreach (var m in dm.Members) if (ImGui.Selectable(m, m == giftRecipient)) giftRecipient = m;
                ImGui.EndCombo();
            }
        }

        var wrap = form.Icon.Length > 0 ? ItemIcons.Wrap(form.Icon) : null;
        if (wrap != null) ImGui.Image(wrap.Handle, new Vector2(Slot, Slot)); else ImGui.Button("?##formicon", new Vector2(Slot, Slot));
        ImGui.SameLine();
        if (ImGui.Button("Choose icon")) ImGui.OpenPopup("pickicon");
        DrawPicker();

        ImGui.SetNextItemWidth(260f); ImGui.InputText("Name##f", ref form.Name, 40);
        ImGui.Text("Description"); ImGui.InputTextMultiline("##fdesc", ref form.Description, 300, new Vector2(ImGui.GetContentRegionAvail().X - 8f, 56f));
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 8f); ImGui.InputText("##fflavor", ref form.Flavor, 150);
        ImGui.TextColored(Dim, "Flavor text, shown in quotes.");

        ImGui.Spacing(); ImGui.Separator(); ImGui.Spacing();
        ImGui.TextColored(Gold, "On use (optional)");
        ImGui.SetNextItemWidth(160f); ImGui.InputText("Button word##fl", ref form.UseLabel, 20);
        ImGui.TextColored(Dim, "e.g. Eat, Read, Open. Leave blank for an item that can't be used.");
        ImGui.Text("What happens"); ImGui.InputTextMultiline("##fuse", ref form.UseText, 400, new Vector2(ImGui.GetContentRegionAvail().X - 8f, 64f));
        ImGui.Checkbox("Used up when used", ref form.Consumable);
        ImGui.Spacing();
        ImGui.SetNextItemWidth(260f); ImGui.InputText("Needs this item##freq", ref form.Requires, 40);
        ImGui.TextColored(Dim, "Optional. The exact name of another item, like The Harbinger's Key. It must be in the user's inventory to use this.");
        if (form.Requires.Trim().Length > 0) ImGui.Checkbox("That item is used up too", ref form.RequiresConsume);
        if (form.Requires.Trim().Length > 0 && form.UseText.Trim().Length == 0) ImGui.TextColored(Red, "Fill in \"What happens\" so there is an action, like Open.");
        ImGui.TextColored(Dim, "To make something hold other items, save this item first, then right-click it and choose Put an item inside.");

        var adm = Plugin.Items.AdminSnapshot();
        if (gift && adm.Error.Length > 0) ImGui.TextColored(Red, adm.Error);
        else if (!gift && s.Error.Length > 0) ImGui.TextColored(Red, s.Error);
        ImGui.Spacing();
        var ok = form.Name.Trim().Length > 0 && form.Icon.Length > 0 && !s.Busy && (!gift || (giftRecipient.Length > 0 && !adm.Busy));
        if (!ok) ImGui.BeginDisabled();
        if (ImGui.Button(gift ? "Send gift" : editingId == null ? "Create item" : "Save changes")) _ = SubmitAsync();
        if (!ok) ImGui.EndDisabled();
        ImGui.SameLine(); if (ImGui.Button("Cancel##form")) mode = Mode.Inventory;
        if (form.Icon.Length == 0) { ImGui.SameLine(); ImGui.TextColored(Dim, "Choose an icon first."); }
    }

    private async System.Threading.Tasks.Task SubmitAsync()
    {
        var f = form; var id = editingId;
        if (mode == Mode.Gift) { if (await Plugin.Items.AdminGrantAsync(f, giftRecipient)) { mode = Mode.Inventory; await Plugin.Items.AdminLoadAsync(); } return; }
        var ok = id == null ? await Plugin.Items.CreateAsync(f) : await Plugin.Items.UpdateAsync(id, f);
        if (ok) mode = Mode.Inventory;
    }

    // ---------- Advocate tools (leader and officers) ----------
    private void DrawDmTools()
    {
        if (!ImGui.CollapsingHeader("Advocate tools")) return;
        ImGui.TextWrapped("For the Harbinger and the Advocates. Uses the creation password.");
        UiUtil.CreatePasswordInput();
        var d = Plugin.Items.AdminSnapshot();
        if (d.Error.Length > 0) ImGui.TextColored(Red, d.Error);
        else if (d.Notice.Length > 0) ImGui.TextColored(Green, d.Notice);
        if (d.Busy) ImGui.TextColored(Dim, "Working...");
        else if (ImGui.Button(d.Loaded ? "Reload lists" : "Load everyone's items")) _ = Plugin.Items.AdminLoadAsync();
        if (!d.Loaded) return;

        ImGui.SameLine();
        if (ImGui.Button("Gift an item...")) { form = new ItemForm(); editingId = null; giftRecipient = ""; mode = Mode.Gift; Plugin.Items.ClearMessages(); }

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

    // ---------- the icon picker ----------
    private void DrawPicker()
    {
        ImGui.SetNextWindowSize(new Vector2(460f, 400f), ImGuiCond.Appearing);
        if (!ImGui.BeginPopup("pickicon")) return;
        if (ImGui.BeginTabBar("pickertabs"))
        {
            if (ImGui.BeginTabItem("Suggested")) { PickerCurated(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Browse game icons")) { PickerBrowse(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("By number")) { PickerNumber(); ImGui.EndTabItem(); }
            ImGui.EndTabBar();
        }
        ImGui.EndPopup();
    }

    private bool IconCell(string icon, string tip, ref int shown)
    {
        var wrap = ItemIcons.Wrap(icon);
        if (wrap == null) return false;
        if (shown % 8 != 0) ImGui.SameLine();
        ImGui.Image(wrap.Handle, new Vector2(40f, 40f));
        shown++;
        if (ImGui.IsItemHovered()) { ImGui.SetMouseCursor(ImGuiMouseCursor.Hand); ImGui.SetTooltip(tip); }
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left)) { form.Icon = icon; ImGui.CloseCurrentPopup(); return true; }
        return false;
    }

    private void PickerCurated()
    {
        var list = Plugin.Data.Icons;
        if (list.Count == 0) { ImGui.TextWrapped("No suggested icons yet. Use Browse game icons or By number."); return; }
        var cats = list.Select(i => i.Category).Where(c => c.Length > 0).Distinct().OrderBy(c => c).ToList();
        cats.Insert(0, "All");
        ImGui.SetNextItemWidth(180f);
        if (ImGui.BeginCombo("Category", category))
        {
            foreach (var c in cats) if (ImGui.Selectable(c, c == category)) category = c;
            ImGui.EndCombo();
        }
        ImGui.BeginChild("curlist", new Vector2(0, 0));
        var shown = 0;
        foreach (var i in list.Where(i => category == "All" || i.Category == category))
            if (IconCell(i.Icon, i.Label.Length > 0 ? i.Label : i.Icon, ref shown)) break;
        ImGui.EndChild();
    }

    private void PickerBrowse()
    {
        ImGui.TextColored(Dim, "Every game icon, 100 numbers at a time. Gaps are numbers with no icon.");
        if (ImGui.Button("< Previous 100") && browseStart >= 100) browseStart -= 100;
        ImGui.SameLine(); if (ImGui.Button("Next 100 >")) browseStart += 100;
        ImGui.SameLine(); ImGui.SetNextItemWidth(90f); ImGui.InputInt("##goto", ref goTo, 0);
        ImGui.SameLine(); if (ImGui.Button("Go") && goTo >= 0) browseStart = goTo / 100 * 100;
        ImGui.Text($"Showing {browseStart} to {browseStart + 99}");
        ImGui.BeginChild("browselist", new Vector2(0, 0));
        var shown = 0;
        for (var id = browseStart; id < browseStart + 100; id++)
            if (IconCell("game:" + id, "Icon " + id, ref shown)) break;
        if (shown == 0) ImGui.TextColored(Dim, "No icons in this range. Try the next page.");
        ImGui.EndChild();
    }

    private void PickerNumber()
    {
        ImGui.TextWrapped("If you know an icon's number, type it here.");
        ImGui.SetNextItemWidth(140f); ImGui.InputInt("Icon number", ref iconNumber, 0);
        var wrap = iconNumber >= 0 ? ItemIcons.Wrap("game:" + iconNumber) : null;
        if (wrap != null)
        {
            ImGui.Image(wrap.Handle, new Vector2(64f, 64f));
            if (ImGui.Button("Use this icon")) { form.Icon = "game:" + iconNumber; ImGui.CloseCurrentPopup(); }
        }
        else ImGui.TextColored(Dim, "No icon with that number.");
    }
}
