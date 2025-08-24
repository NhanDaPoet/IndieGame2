using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(CraftingRecipe))]
public class CraftingRecipeEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // Lấy đối tượng CraftingRecipe
        CraftingRecipe recipe = (CraftingRecipe)target;

        // Hiển thị lưới 3x3
        EditorGUILayout.LabelField("Crafting Grid (3x3)", EditorStyles.boldLabel);
        
        // Lấy SerializedProperty của gridData (mảng 1 chiều)
        SerializedProperty gridDataProp = serializedObject.FindProperty("gridData");
        
        if (gridDataProp == null)
        {
            EditorGUILayout.HelpBox("GridData property not found! Ensure the gridData field is properly defined in CraftingRecipe.", MessageType.Error);
            return;
        }

        // Kiểm tra kích thước mảng
        if (gridDataProp.arraySize != 9) // 3x3 = 9 phần tử
        {
            EditorGUILayout.HelpBox("Grid array size is incorrect! Expected 9 elements for a 3x3 grid.", MessageType.Warning);
            if (GUILayout.Button("Fix Array Size"))
            {
                gridDataProp.arraySize = 9;
                serializedObject.ApplyModifiedProperties();
            }
        }
        else
        {
            // Tạo style cho grid container
            GUIStyle gridContainerStyle = new GUIStyle();
            gridContainerStyle.padding = new RectOffset(5, 5, 5, 5);
            
            EditorGUILayout.BeginVertical(gridContainerStyle);
            
            // Hiển thị grid 3x3 với kích thước cố định
            for (int i = 0; i < 3; i++)
            {
                EditorGUILayout.BeginHorizontal();
                
                for (int j = 0; j < 3; j++)
                {
                    // Style cho mỗi slot
                    GUIStyle slotStyle = new GUIStyle(GUI.skin.box);
                    slotStyle.padding = new RectOffset(8, 8, 8, 8);
                    slotStyle.margin = new RectOffset(2, 2, 2, 2);
                    
                    EditorGUILayout.BeginVertical(slotStyle, GUILayout.Width(140), GUILayout.Height(80));
                    
                    // Truy cập phần tử grid[i,j] từ mảng 1 chiều
                    int index = i * 3 + j;
                    SerializedProperty slot = gridDataProp.GetArrayElementAtIndex(index);
                    
                    if (slot != null)
                    {
                        // Header cho slot
                        EditorGUILayout.LabelField($"Slot [{i},{j}]", EditorStyles.centeredGreyMiniLabel);
                        
                        SerializedProperty itemIdProp = slot.FindPropertyRelative("itemId");
                        SerializedProperty quantityProp = slot.FindPropertyRelative("quantity");
                        
                        if (itemIdProp != null && quantityProp != null)
                        {
                            EditorGUILayout.BeginHorizontal();
                            EditorGUILayout.LabelField("ID:", GUILayout.Width(25));
                            itemIdProp.intValue = EditorGUILayout.IntField(itemIdProp.intValue, GUILayout.Width(60));
                            EditorGUILayout.EndHorizontal();
                            
                            EditorGUILayout.BeginHorizontal();
                            EditorGUILayout.LabelField("Qty:", GUILayout.Width(25));
                            quantityProp.intValue = EditorGUILayout.IntField(quantityProp.intValue, GUILayout.Width(60));
                            EditorGUILayout.EndHorizontal();
                        }
                        else
                        {
                            EditorGUILayout.LabelField("Property error!", EditorStyles.miniLabel);
                        }
                    }
                    else
                    {
                        EditorGUILayout.LabelField("Slot not initialized!", EditorStyles.miniLabel);
                    }
                    
                    EditorGUILayout.EndVertical();
                }
                
                EditorGUILayout.EndHorizontal();
            }
            
            EditorGUILayout.EndVertical();
        }

        EditorGUILayout.Space(10);
        
        // Separator line
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        EditorGUILayout.Space(5);
        
        // Hiển thị trường Result
        EditorGUILayout.LabelField("Recipe Result", EditorStyles.boldLabel);
        SerializedProperty resultProp = serializedObject.FindProperty("result");
        if (resultProp != null)
        {
            EditorGUILayout.PropertyField(resultProp, new GUIContent("Result"), true);
        }
        else
        {
            EditorGUILayout.HelpBox("Result property not found!", MessageType.Warning);
        }

        EditorGUILayout.Space(10);
        
        // Button để clear toàn bộ grid
        if (GUILayout.Button("Clear All Slots", GUILayout.Height(25)))
        {
            for (int i = 0; i < 9; i++)
            {
                SerializedProperty slot = gridDataProp.GetArrayElementAtIndex(i);
                if (slot != null)
                {
                    slot.FindPropertyRelative("itemId").intValue = 0;
                    slot.FindPropertyRelative("quantity").intValue = 0;
                }
            }
            GUI.changed = true;
        }

        // Áp dụng các thay đổi
        if (GUI.changed)
        {
            EditorUtility.SetDirty(target);
        }
        
        serializedObject.ApplyModifiedProperties();
    }

    // Override để tăng chiều cao của inspector
    public override bool RequiresConstantRepaint()
    {
        return false;
    }
}