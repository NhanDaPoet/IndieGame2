// Editor/CraftingRecipeEditor.cs
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

[CustomEditor(typeof(CraftingRecipe))]
public class CraftingRecipeEditor : Editor
{
    // === Picker cache ===
    private ItemDatabase pickerDb;
    private ItemData[] dbItems = System.Array.Empty<ItemData>();
    private string[] dbItemDisplay = new string[] { "None (0)" };
    private int[] dbItemIds = new int[] { 0 };
    private Dictionary<int, int> idToPopupIndex = new Dictionary<int, int>();

    private string search = "";
    private ReorderableList shapelessList;

    private SerializedProperty gridDataProp;
    private SerializedProperty resultProp;
    private SerializedProperty isShapelessProp;
    private SerializedProperty shapelessReqProp;

    private const float SlotW = 50f;
    private const float SlotH = 50f;

    private GUIStyle slotBox;
    private GUIStyle centeredMini;

    private GUIStyle toolbarSearchTextField;
    private GUIStyle toolbarSearchCancelButton;

    private Vector2 gridScroll;

    private void OnEnable()
    {
        gridDataProp = serializedObject.FindProperty("gridData");
        resultProp = serializedObject.FindProperty("result");
        isShapelessProp = serializedObject.FindProperty("isShapeless");
        shapelessReqProp = serializedObject.FindProperty("shapelessRequirements");
        if (pickerDb == null)
        {
            var guids = AssetDatabase.FindAssets("t:ItemDatabase");
            if (guids != null && guids.Length > 0)
                pickerDb = AssetDatabase.LoadAssetAtPath<ItemDatabase>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }
        RebuildPickerCache();

        if (shapelessReqProp != null)
        {
            shapelessList = new ReorderableList(serializedObject, shapelessReqProp, true, true, true, true);
            shapelessList.drawHeaderCallback = r => EditorGUI.LabelField(r, "Shapeless Requirements (id + quantity)");
            shapelessList.elementHeight = EditorGUIUtility.singleLineHeight * 2 + 12f;
            shapelessList.drawElementCallback = (rect, index, active, focused) =>
            {
                var el = shapelessReqProp.GetArrayElementAtIndex(index);
                var idProp = el.FindPropertyRelative("itemId");
                var qtyProp = el.FindPropertyRelative("quantity");
                rect.y += 2f;
                Rect r1L = new Rect(rect.x, rect.y, rect.width * 0.58f, EditorGUIUtility.singleLineHeight);
                Rect r1R = new Rect(rect.x + rect.width * 0.6f, rect.y, rect.width * 0.4f, EditorGUIUtility.singleLineHeight);
                int curId = Mathf.Max(0, idProp.intValue);
                int curIx = IdToIndex(curId);
                int newIx = EditorGUI.Popup(r1L, "Item", curIx, dbItemDisplay);
                if (newIx != curIx) idProp.intValue = dbItemIds[newIx];
                ItemData obj = EditorGUI.ObjectField(r1R, GUIContent.none, IdToItem(curId), typeof(ItemData), false) as ItemData;
                if (obj != null) idProp.intValue = obj.id;
                Rect r2 = new Rect(rect.x, rect.y + EditorGUIUtility.singleLineHeight + 6f, rect.width * 0.5f, EditorGUIUtility.singleLineHeight);
                qtyProp.intValue = Mathf.Max(1, EditorGUI.IntField(r2, "Quantity", Mathf.Max(1, qtyProp.intValue)));
            };
        }
    }

    public override void OnInspectorGUI()
    {
        EnsureStyles_InGUI();
        serializedObject.Update();
        DrawDatabasePicker();
        EditorGUILayout.Space(4);
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.PropertyField(isShapelessProp, new GUIContent("Shapeless Recipe"));
            GUILayout.FlexibleSpace();
            search = SearchField(search, GUILayout.Width(220));
        }
        EditorGUILayout.HelpBox(isShapelessProp.boolValue
            ? "Shapeless: chỉ quan tâm tổng số lượng từng itemId, không quan tâm vị trí."
            : "Shaped: 3×3, auto-trim bounding box; đặt lệch góc vẫn khớp.", MessageType.Info);
        EditorGUILayout.Space(6);

        if (isShapelessProp.boolValue) DrawShapeless();
        else DrawShapedGrid();
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        EditorGUILayout.LabelField("Recipe Result", EditorStyles.boldLabel);
        if (resultProp != null)
        {
            using (new EditorGUILayout.VerticalScope(GUI.skin.box))
            {
                EditorGUILayout.PropertyField(resultProp, GUIContent.none, true);
                var itemIdProp = resultProp.FindPropertyRelative("itemId");
                var qtyProp = resultProp.FindPropertyRelative("quantity");
                var previewItem = IdToItem(itemIdProp != null ? itemIdProp.intValue : 0);
                if (previewItem != null && previewItem.icon != null)
                {
                    Rect r = GUILayoutUtility.GetRect(56, 56, GUILayout.ExpandWidth(false));
                    GUI.DrawTexture(r, previewItem.icon.texture, ScaleMode.ScaleToFit);
                    EditorGUI.LabelField(new Rect(r.xMax + 6, r.y, 220, 18),
                        $"{previewItem.itemName} (id: {previewItem.id})");
                    EditorGUI.LabelField(new Rect(r.xMax + 6, r.y + 18, 120, 18),
                        $"Qty: {(qtyProp != null ? qtyProp.intValue : 0)}");
                }
            }
        }
        else
        {
            EditorGUILayout.HelpBox("Result property not found!", MessageType.Warning);
        }
        if (GUI.changed) EditorUtility.SetDirty(target);
        serializedObject.ApplyModifiedProperties();
    }

    // === Shaped ===
    private void DrawShapedGrid()
    {
        if (gridDataProp == null)
        {
            EditorGUILayout.HelpBox("gridData not found.", MessageType.Error);
            return;
        }
        if (gridDataProp.arraySize != 9)
        {
            EditorGUILayout.HelpBox("Grid array size incorrect (need 9).", MessageType.Warning);
            if (GUILayout.Button("Fix Array Size")) gridDataProp.arraySize = 9;
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Trim Shape", GUILayout.Height(22))) TrimGrid();
            if (GUILayout.Button("Rotate 90° CW", GUILayout.Height(22))) RotateGridCW();
            if (GUILayout.Button("Mirror H", GUILayout.Height(22))) MirrorGridH();
            if (GUILayout.Button("Mirror V", GUILayout.Height(22))) MirrorGridV();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Clear All", GUILayout.Height(22))) ClearGrid();
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Convert to Shapeless", GUILayout.Height(22))) ConvertShapedToShapeless();
            GUILayout.FlexibleSpace();
        }

        EditorGUILayout.Space(6);

        float viewW = EditorGUIUtility.currentViewWidth;    
        float padding = 24f;
        float available = Mathf.Max(200f, viewW - padding);
        float colW = Mathf.Max(140f, Mathf.Floor((available - 2f * 8f) / 3f)); 
        float colH = Mathf.Max(90f, colW * 0.6f + 40f);
        float minTotalW = colW * 3f + 2f * 8f + 8f; 
        bool needScroll = available < minTotalW;
        if (needScroll)
            gridScroll = EditorGUILayout.BeginScrollView(gridScroll, GUILayout.Height(colH * 3f + 40f)); 
        for (int i = 0; i < 3; i++)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                for (int j = 0; j < 3; j++)
                {
                    DrawShapedSlot(i, j, colW, colH);
                }
                GUILayout.FlexibleSpace();
            }
        }
        if (needScroll)
            EditorGUILayout.EndScrollView();
    }

    private void DrawShapedSlot(int i, int j, float w, float h)
    {
        int index = i * 3 + j;
        var slot = gridDataProp.GetArrayElementAtIndex(index);
        using (new EditorGUILayout.VerticalScope(slotBox, GUILayout.Width(w), GUILayout.Height(h)))
        {
            if (slot == null) { EditorGUILayout.LabelField("Slot not initialized!", centeredMini); return; }
            var itemIdProp = slot.FindPropertyRelative("itemId");
            var qtyProp = slot.FindPropertyRelative("quantity");
            if (itemIdProp == null || qtyProp == null) { EditorGUILayout.LabelField("Property error!", centeredMini); return; }
            EditorGUILayout.LabelField($"Slot [{i},{j}]", centeredMini);
            int curId = Mathf.Max(0, itemIdProp.intValue);
            var curItem = IdToItem(curId);
            Texture2D icon = (curItem != null && curItem.icon != null) ? curItem.icon.texture : null;
            Rect iconRect = GUILayoutUtility.GetRect(18, 18, GUILayout.ExpandWidth(false));
            if (icon != null) GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
            float nameW = Mathf.Max(60f, w - iconRect.width - 36f);
            Rect nameRect = new Rect(iconRect.xMax + 6, iconRect.y + 1, nameW, 18);
            EditorGUI.LabelField(nameRect, curItem != null ? curItem.itemName : "(empty)");
            int curIx = IdToIndex(curId);
            int newIx = EditorGUILayout.Popup("Item", curIx, dbItemDisplay);
            if (newIx != curIx) itemIdProp.intValue = dbItemIds[newIx];
            ItemData dragObj = (ItemData)EditorGUILayout.ObjectField("Asset", curItem, typeof(ItemData), false);
            if (dragObj != null) itemIdProp.intValue = dragObj.id;
            using (new EditorGUILayout.HorizontalScope())
            {
                qtyProp.intValue = Mathf.Max(0, EditorGUILayout.IntField("Qty", Mathf.Max(0, qtyProp.intValue)));
                if (GUILayout.Button("×", GUILayout.Width(22)))
                {
                    itemIdProp.intValue = 0;
                    qtyProp.intValue = 0;
                }
            }
        }
    }

    // === Shapeless ===
    private void DrawShapeless()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Clear Shapeless", GUILayout.Height(22)))
                shapelessReqProp.arraySize = 0;
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Convert to Shaped (fill L→R, Top→Down)", GUILayout.Height(22)))
            {
                ConvertShapelessToShaped();
                isShapelessProp.boolValue = false;
            }
        }
        EditorGUILayout.Space(4);
        if (shapelessList != null)
        {
            shapelessList.DoLayoutList();
            if (GUILayout.Button("Merge same itemId", GUILayout.Height(22)))
                MergeDuplicateShapeless();
        }
        else
        {
            EditorGUILayout.HelpBox("shapelessRequirements not found.", MessageType.Error);
        }
    }

    // === Commands ===
    private void ClearGrid()
    {
        for (int i = 0; i < 9; i++)
        {
            var slot = gridDataProp.GetArrayElementAtIndex(i);
            if (slot == null) continue;
            var id = slot.FindPropertyRelative("itemId");
            var qty = slot.FindPropertyRelative("quantity");
            if (id != null) id.intValue = 0;
            if (qty != null) qty.intValue = 0;
        }
    }

    private void TrimGrid()
    {
        int[,] ids = new int[3, 3];
        int[,] qtys = new int[3, 3];
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
            {
                var slot = gridDataProp.GetArrayElementAtIndex(i * 3 + j);
                ids[i, j] = slot.FindPropertyRelative("itemId").intValue;
                qtys[i, j] = slot.FindPropertyRelative("quantity").intValue;
            }
        int rMin = 3, rMax = -1, cMin = 3, cMax = -1;
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
            {
                if (ids[i, j] != 0)
                {
                    if (i < rMin) rMin = i; if (i > rMax) rMax = i;
                    if (j < cMin) cMin = j; if (j > cMax) cMax = j;
                }
            }
        if (rMax < rMin) return;
        int h = rMax - rMin + 1;
        int w = cMax - cMin + 1;
        int[,] nids = new int[3, 3];
        int[,] nqty = new int[3, 3];
        for (int dr = 0; dr < h; dr++)
            for (int dc = 0; dc < w; dc++)
            {
                nids[dr, dc] = ids[rMin + dr, cMin + dc];
                nqty[dr, dc] = qtys[rMin + dr, cMin + dc];
            }
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
            {
                var slot = gridDataProp.GetArrayElementAtIndex(i * 3 + j);
                slot.FindPropertyRelative("itemId").intValue = nids[i, j];
                slot.FindPropertyRelative("quantity").intValue = nqty[i, j];
            }
    }

    private void RotateGridCW()
    {
        int[,] ids = new int[3, 3];
        int[,] qtys = new int[3, 3];
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
            {
                var slot = gridDataProp.GetArrayElementAtIndex(i * 3 + j);
                ids[i, j] = slot.FindPropertyRelative("itemId").intValue;
                qtys[i, j] = slot.FindPropertyRelative("quantity").intValue;
            }
        int[,] nids = new int[3, 3];
        int[,] nqty = new int[3, 3];
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
            {
                nids[j, 2 - i] = ids[i, j];
                nqty[j, 2 - i] = qtys[i, j];
            }
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
            {
                var slot = gridDataProp.GetArrayElementAtIndex(i * 3 + j);
                slot.FindPropertyRelative("itemId").intValue = nids[i, j];
                slot.FindPropertyRelative("quantity").intValue = nqty[i, j];
            }
    }

    private void MirrorGridH()
    {
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3 / 2; j++)
            {
                int a = i * 3 + j;
                int b = i * 3 + (2 - j);
                SwapSlots(a, b);
            }
    }
    private void MirrorGridV()
    {
        for (int i = 0; i < 3 / 2; i++)
            for (int j = 0; j < 3; j++)
            {
                int a = i * 3 + j;
                int b = (2 - i) * 3 + j;
                SwapSlots(a, b);
            }
    }
    private void SwapSlots(int a, int b)
    {
        var sa = gridDataProp.GetArrayElementAtIndex(a);
        var sb = gridDataProp.GetArrayElementAtIndex(b);
        int ida = sa.FindPropertyRelative("itemId").intValue;
        int qa = sa.FindPropertyRelative("quantity").intValue;
        int idb = sb.FindPropertyRelative("itemId").intValue;
        int qb = sb.FindPropertyRelative("quantity").intValue;
        sa.FindPropertyRelative("itemId").intValue = idb;
        sa.FindPropertyRelative("quantity").intValue = qb;
        sb.FindPropertyRelative("itemId").intValue = ida;
        sb.FindPropertyRelative("quantity").intValue = qa;
    }

    private void ConvertShapedToShapeless()
    {
        Dictionary<int, int> agg = new Dictionary<int, int>();
        for (int i = 0; i < 9; i++)
        {
            var slot = gridDataProp.GetArrayElementAtIndex(i);
            int id = slot.FindPropertyRelative("itemId").intValue;
            int q = slot.FindPropertyRelative("quantity").intValue;
            if (id <= 0 || q <= 0) continue;
            if (!agg.ContainsKey(id)) agg[id] = 0;
            agg[id] += q;
        }
        shapelessReqProp.arraySize = 0;
        foreach (var kv in agg)
        {
            int n = shapelessReqProp.arraySize;
            shapelessReqProp.arraySize = n + 1;
            var el = shapelessReqProp.GetArrayElementAtIndex(n);
            el.FindPropertyRelative("itemId").intValue = kv.Key;
            el.FindPropertyRelative("quantity").intValue = kv.Value;
        }
        isShapelessProp.boolValue = true;
    }

    private void ConvertShapelessToShaped()
    {
        ClearGrid();
        int cursor = 0;
        for (int i = 0; i < shapelessReqProp.arraySize; i++)
        {
            var el = shapelessReqProp.GetArrayElementAtIndex(i);
            int id = el.FindPropertyRelative("itemId").intValue;
            int qty = Mathf.Max(1, el.FindPropertyRelative("quantity").intValue);

            while (qty > 0 && cursor < 9)
            {
                var slot = gridDataProp.GetArrayElementAtIndex(cursor);
                slot.FindPropertyRelative("itemId").intValue = id;
                slot.FindPropertyRelative("quantity").intValue = 1; 
                qty--;
                cursor++;
            }
            if (cursor >= 9) break;
        }
    }

    private void MergeDuplicateShapeless()
    {
        Dictionary<int, int> agg = new Dictionary<int, int>();
        for (int i = 0; i < shapelessReqProp.arraySize; i++)
        {
            var el = shapelessReqProp.GetArrayElementAtIndex(i);
            int id = el.FindPropertyRelative("itemId").intValue;
            int q = el.FindPropertyRelative("quantity").intValue;
            if (id <= 0 || q <= 0) continue;
            if (!agg.ContainsKey(id)) agg[id] = 0;
            agg[id] += q;
        }
        shapelessReqProp.arraySize = 0;
        foreach (var kv in agg)
        {
            int n = shapelessReqProp.arraySize;
            shapelessReqProp.arraySize = n + 1;
            var el = shapelessReqProp.GetArrayElementAtIndex(n);
            el.FindPropertyRelative("itemId").intValue = kv.Key;
            el.FindPropertyRelative("quantity").intValue = kv.Value;
        }
    }

    // === Picker / DB ===
    private void DrawDatabasePicker()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            pickerDb = (ItemDatabase)EditorGUILayout.ObjectField("Item Database (picker)", pickerDb, typeof(ItemDatabase), false);
            if (GUILayout.Button("Auto", GUILayout.Width(56)))
            {
                string[] guids = AssetDatabase.FindAssets("t:ItemDatabase");
                if (guids != null && guids.Length > 0)
                    pickerDb = AssetDatabase.LoadAssetAtPath<ItemDatabase>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }
            if (GUILayout.Button("Reload", GUILayout.Width(68)))
            {
                RebuildPickerCache();
            }
        }
        if (pickerDb == null)
        {
            EditorGUILayout.HelpBox("Chưa chọn ItemDatabase → popup chỉ hiển thị 'None'.", MessageType.Warning);
        }
    }

    private void RebuildPickerCache()
    {
        if (pickerDb != null)
            dbItems = pickerDb.GetAllItems() ?? System.Array.Empty<ItemData>();
        else
            dbItems = System.Array.Empty<ItemData>();
        var itemsFiltered = dbItems.Where(x => x != null).OrderBy(x => x.id).ToList();
        var names = new List<string>() { "None (0)" };
        var ids = new List<int>() { 0 };
        foreach (var it in itemsFiltered)
        {
            names.Add($"[{it.id:D4}] {it.itemName}");
            ids.Add(it.id);
        }
        dbItemDisplay = names.ToArray();
        dbItemIds = ids.ToArray();
        idToPopupIndex.Clear();
        for (int i = 0; i < dbItemIds.Length; i++)
            idToPopupIndex[dbItemIds[i]] = i;
    }

    private int IdToIndex(int id) => idToPopupIndex.TryGetValue(id, out int ix) ? ix : 0;
    private ItemData IdToItem(int id)
    {
        if (pickerDb == null || id <= 0) return null;
        return pickerDb.GetItemData(id);
    }

    // === Styles & Search field (robust across skins/versions) ===
    private void EnsureStyles()
    {
        if (slotBox == null)
            slotBox = new GUIStyle(GUI.skin.box) { padding = new RectOffset(8, 8, 6, 6), margin = new RectOffset(3, 3, 3, 3) };
        if (centeredMini == null)
            centeredMini = new GUIStyle(EditorStyles.centeredGreyMiniLabel);
        toolbarSearchTextField = GUI.skin.FindStyle("ToolbarSearchTextField") ?? EditorStyles.toolbarSearchField ?? GUI.skin.textField;
        toolbarSearchCancelButton = GUI.skin.FindStyle("ToolbarSearchCancelButton") ?? EditorStyles.toolbarButton;
    }
    private void EnsureStyles_InGUI()
    {
        if (slotBox == null)
            slotBox = new GUIStyle(GUI.skin.box) { padding = new RectOffset(8, 8, 6, 6), margin = new RectOffset(3, 3, 3, 3) };
        if (centeredMini == null)
            centeredMini = new GUIStyle(EditorStyles.centeredGreyMiniLabel);
        toolbarSearchTextField = GUI.skin.FindStyle("ToolbarSearchTextField")
                                       ?? EditorStyles.toolbarSearchField
                                       ?? GUI.skin.textField;
        toolbarSearchCancelButton = GUI.skin.FindStyle("ToolbarSearchCancelButton")
                                       ?? EditorStyles.toolbarButton;
    }


    private string SearchField(string s, params GUILayoutOption[] opts)
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            s = GUILayout.TextField(s, toolbarSearchTextField, opts);
            if (GUILayout.Button(string.IsNullOrEmpty(s) ? " " : "×", toolbarSearchCancelButton, GUILayout.Width(20)))
            {
                s = "";
                GUI.FocusControl(null);
            }
        }
        return s;
    }
}
