using UnityEngine;

[CreateAssetMenu(fileName = "ResourceDefinition", menuName = "Resource/Definition")]
public class ResourceDefinition : ScriptableObject
{
    public ResourceKind kind = ResourceKind.Tree;
    public int tier = 1;

    [Header("Yêu cầu Tool")]
    [Tooltip("So khớp với ItemData.material của Tool (vd: \"Axe\", \"Pickaxe\", \"Sickle\").")]
    public string requiredToolMaterial = ToolTags.Axe;

    [Tooltip("Độ hiếm tối thiểu của Tool (Common/Uncommon/...).")]
    public ItemRarity minToolRarity = ItemRarity.Common;

    [Header("Sức bền / Charges")]
    [Min(1)] public int maxHealth = 10;

    [Header("Depletion Visual Stages")]
    [Tooltip("Các mốc % còn lại để đổi sprite/visual; ví dụ: 0.7, 0.4, 0.1")]
    public float[] depletionThresholds = new float[] { 0.7f, 0.4f, 0.1f };
    public Sprite[] depletionSprites;

    [Header("Depletion Effect")]
    [Tooltip("GameObject prefab containing particle system to play when transitioning between depletion stages.")]
    public GameObject[] stageTransitionEffectPrefabs;

    [Tooltip("Single fallback effect if you want to use the same effect for all transitions")]
    public GameObject defaultStageTransitionEffect;

    [Header("Gathering")]
    public GatherMode gatherMode = GatherMode.Shared;
    [Tooltip("Thời gian tối thiểu giữa 2 lần tiến trình khi Shared (giảm tải).")]
    [Range(0.05f, 0.5f)]
    public float sharedTickInterval = 0.15f;

    [Tooltip("Thời gian tối thiểu giữa 2 lần Hit trong Solo.")]
    [Range(0.05f, 0.75f)]
    public float soloHitInterval = 0.25f;

    [Header("Loot")]
    public LootTable lootTable;

    [Header("Tham chiếu DB")]
    public ItemDatabase itemDatabase;

    public GameObject GetStageTransitionEffect(int stageIndex)
    {
        if (stageTransitionEffectPrefabs != null &&
            stageIndex >= 0 &&
            stageIndex < stageTransitionEffectPrefabs.Length)
        {
            return stageTransitionEffectPrefabs[stageIndex];
        }
        return defaultStageTransitionEffect;
    }

    public int GetMaxStageTransitions()
    {
        if (stageTransitionEffectPrefabs != null)
            return stageTransitionEffectPrefabs.Length;

        if (depletionThresholds != null)
            return depletionThresholds.Length;

        return 0;
    }
}