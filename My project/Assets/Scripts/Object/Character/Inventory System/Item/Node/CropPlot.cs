using Mirror;
using UnityEngine;

public class CropPlot : ResourceNodeBase
{
    [Header("Growth")]
    [Tooltip("Thời gian mỗi giai đoạn tính theo giây.")]
    public float[] growthStageDurations = new float[] { 30f, 60f, 90f };

    [Tooltip("Sprite hiển thị theo giai đoạn tăng trưởng (không trùng với DepletionSprites).")]
    public Sprite[] growthSprites;

    [Header("Harvest Window")]
    [Tooltip("Sau khi đạt giai đoạn chín (cuối), trong khoảng thời gian này mới cho phép thu hoạch.")]
    public float harvestWindowSeconds = 60f;

    [SyncVar] private int growthStage; // 0..N
    [SyncVar] private double stageStartNetworkTime;
    [SyncVar] private bool isHarvestable;

    public override void OnStartServer()
    {
        base.OnStartServer();
        growthStage = 0;
        stageStartNetworkTime = NetworkTime.time;
        isHarvestable = false;
    }

    [ServerCallback]
    private void LateUpdate()
    {
        if (!NetworkServer.active) return;
        TickGrowth();
    }

    [Server]
    private void TickGrowth()
    {
        if (growthStageDurations == null || growthStageDurations.Length == 0) return;

        if (growthStage < growthStageDurations.Length)
        {
            double elapsed = NetworkTime.time - stageStartNetworkTime;
            if (elapsed >= growthStageDurations[growthStage])
            {
                growthStage++;
                stageStartNetworkTime = NetworkTime.time;

                UpdateGrowthVisual();
                if (growthStage >= growthStageDurations.Length)
                {
                    isHarvestable = true;
                }
            }
        }
        else
        {
            double elapsedSinceRipe = NetworkTime.time - stageStartNetworkTime;
            if (elapsedSinceRipe > harvestWindowSeconds)
            {
                isHarvestable = false;
            }
        }
    }

    [Server]
    protected override void ServerOnDepleted(PlayerMovement sourcePlayer)
    {
        if (!isHarvestable)
        {
            return;
        }
        base.ServerOnDepleted(sourcePlayer);
        isHarvestable = false;
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        UpdateGrowthVisual();
    }

    private void UpdateGrowthVisual()
    {
        var sr = GetComponent<SpriteRenderer>();
        if (sr == null) return;

        if (growthSprites != null && growthSprites.Length > 0)
        {
            int idx = Mathf.Clamp(growthStage, 0, growthSprites.Length - 1);
            sr.sprite = growthSprites[idx];
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = isHarvestable ? Color.green : Color.yellow;
        Gizmos.DrawWireCube(transform.position + Vector3.up * 0.1f, new Vector3(1f, 0.25f, 0f));
    }
}
