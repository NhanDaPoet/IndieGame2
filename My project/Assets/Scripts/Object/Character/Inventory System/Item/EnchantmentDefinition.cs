using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "EnchantDef", menuName = "Enchant/Definition")]
public class EnchantmentDefinition : ScriptableObject
{
    public int id;
    public string displayName;

    [Header("Targets")]
    public bool allowWeapon = true;
    public bool allowArmor = false;
    public bool allowAccessory = false;
    public bool allowTool = false;

    [Header("Level")]
    [Min(1)] public int minLevel = 1;
    [Range(1, 7)] public int maxLevel = 7;

    [Header("Conflicts")]
    [Tooltip("Các enchant không thể đi kèm.")]
    public List<EnchantmentDefinition> conflicts = new();

    [Header("Effects")]
    public List<StatEffect> effects = new(); 

    [Serializable]
    public struct StatEffect
    {
        public string key;
        public float valuePerLevel;
    }

    public bool IsAllowedFor(ItemType type)
    {
        return (type == ItemType.Weapon && allowWeapon)
            || (type == ItemType.Tool && allowTool)
            || (type == ItemType.Consumable && false)      
            || (type == ItemType.Material && false)        
            || (type.ToString() == "Armor" && allowArmor)  
            || (type.ToString() == "Accessory" && allowAccessory);
    }
}
[Serializable]
public struct EnchantmentInstance
{
    public int enchantId;
    public int level;
}