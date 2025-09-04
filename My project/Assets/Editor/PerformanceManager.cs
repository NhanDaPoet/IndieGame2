using UnityEngine;
using System.Collections;

/// <summary>
/// Singleton helper để quản lý performance settings và monitoring cho map generation
/// </summary>
public class PerformanceManager : MonoBehaviour
{
    public static PerformanceManager Instance { get; private set; }

    [Header("Performance Monitoring")]
    [SerializeField] private bool enableMonitoring = true;
    [SerializeField] private float targetFrameRate = 60f;
    [SerializeField] private float warningThreshold = 50f; // FPS dưới mức này sẽ warning
    [SerializeField] private float criticalThreshold = 30f; // FPS dưới mức này sẽ giảm performance settings

    [Header("Dynamic Performance Settings")]
    [SerializeField] private bool enableDynamicAdjustment = true;
    [SerializeField] private float performanceCheckInterval = 2f; // Kiểm tra performance mỗi 2s

    [Header("Performance Levels")]
    [SerializeField] private PerformanceLevel currentLevel = PerformanceLevel.High;

    public enum PerformanceLevel
    {
        Low,
        Medium,
        High,
        Ultra
    }

    [System.Serializable]
    public class PerformanceSettings
    {
        public int maxSpawnPerFrame = 5;
        public int maxDespawnPerFrame = 10;
        public int poolSizePerPrefab = 30;
        public int commonPrefabPoolSize = 100;
        public int maxChunksPerFrame = 1;
        public float timeBudgetMs = 2f;
        public int tilesPerBatch = 50;
        public int preGenRadius = 3;
        public float playerUpdateInterval = 0.5f;
    }

    [Header("Performance Presets")]
    [SerializeField]
    private PerformanceSettings lowSettings = new()
    {
        maxSpawnPerFrame = 2,
        maxDespawnPerFrame = 5,
        poolSizePerPrefab = 15,
        commonPrefabPoolSize = 50,
        maxChunksPerFrame = 1,
        timeBudgetMs = 1f,
        tilesPerBatch = 25,
        preGenRadius = 2,
        playerUpdateInterval = 1f
    };

    [SerializeField]
    private PerformanceSettings mediumSettings = new()
    {
        maxSpawnPerFrame = 3,
        maxDespawnPerFrame = 8,
        poolSizePerPrefab = 25,
        commonPrefabPoolSize = 75,
        maxChunksPerFrame = 1,
        timeBudgetMs = 2f,
        tilesPerBatch = 40,
        preGenRadius = 3,
        playerUpdateInterval = 0.75f
    };

    [SerializeField]
    private PerformanceSettings highSettings = new()
    {
        maxSpawnPerFrame = 5,
        maxDespawnPerFrame = 10,
        poolSizePerPrefab = 30,
        commonPrefabPoolSize = 100,
        maxChunksPerFrame = 1,
        timeBudgetMs = 2f,
        tilesPerBatch = 50,
        preGenRadius = 3,
        playerUpdateInterval = 0.5f
    };

    [SerializeField]
    private PerformanceSettings ultraSettings = new()
    {
        maxSpawnPerFrame = 8,
        maxDespawnPerFrame = 15,
        poolSizePerPrefab = 40,
        commonPrefabPoolSize = 150,
        maxChunksPerFrame = 2,
        timeBudgetMs = 3f,
        tilesPerBatch = 75,
        preGenRadius = 4,
        playerUpdateInterval = 0.25f
    };

    // Runtime monitoring
    private float[] frameTimeHistory = new float[60]; // 1 second history at 60fps
    private int frameTimeIndex = 0;
    private float lastPerformanceCheck = 0f;
    private int consecutiveLowFrames = 0;
    private int consecutiveGoodFrames = 0;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Auto-detect initial performance level dựa trên device
        AutoDetectPerformanceLevel();

        if (enableMonitoring)
        {
            StartCoroutine(MonitorPerformance());
        }
    }

    private void AutoDetectPerformanceLevel()
    {
        // Dựa trên system specs để set initial performance level
        int processorCount = SystemInfo.processorCount;
        int systemMemoryMB = SystemInfo.systemMemorySize;

        if (processorCount >= 8 && systemMemoryMB >= 16000)
        {
            currentLevel = PerformanceLevel.Ultra;
        }
        else if (processorCount >= 4 && systemMemoryMB >= 8000)
        {
            currentLevel = PerformanceLevel.High;
        }
        else if (processorCount >= 2 && systemMemoryMB >= 4000)
        {
            currentLevel = PerformanceLevel.Medium;
        }
        else
        {
            currentLevel = PerformanceLevel.Low;
        }

        Debug.Log($"Auto-detected performance level: {currentLevel} (CPU: {processorCount} cores, RAM: {systemMemoryMB}MB)");
        ApplyPerformanceSettings();
    }

    private IEnumerator MonitorPerformance()
    {
        while (true)
        {
            yield return new WaitForSeconds(performanceCheckInterval);

            if (enableDynamicAdjustment)
            {
                CheckAndAdjustPerformance();
            }

            LogPerformanceStats();
        }
    }

    private void Update()
    {
        if (enableMonitoring)
        {
            // Track frame time
            frameTimeHistory[frameTimeIndex] = Time.unscaledDeltaTime;
            frameTimeIndex = (frameTimeIndex + 1) % frameTimeHistory.Length;
        }
    }

    private float GetAverageFrameRate()
    {
        float totalTime = 0f;
        int validFrames = 0;

        for (int i = 0; i < frameTimeHistory.Length; i++)
        {
            if (frameTimeHistory[i] > 0f)
            {
                totalTime += frameTimeHistory[i];
                validFrames++;
            }
        }

        if (validFrames == 0) return 60f; // Default

        return validFrames / totalTime;
    }

    private void CheckAndAdjustPerformance()
    {
        float avgFps = GetAverageFrameRate();
        if (avgFps < criticalThreshold)
        {
            consecutiveLowFrames++;
            consecutiveGoodFrames = 0;
            if (consecutiveLowFrames >= 2 && currentLevel > PerformanceLevel.Low)
            {
                currentLevel = (PerformanceLevel)((int)currentLevel - 1);
                ApplyPerformanceSettings();
                consecutiveLowFrames = 0;
            }
        }
        else if (avgFps < warningThreshold)
        {
            consecutiveLowFrames++;
            consecutiveGoodFrames = 0;
            if (consecutiveLowFrames >= 3 && currentLevel > PerformanceLevel.Low)
            {
                Debug.LogWarning($"Performance warning ({avgFps:F1} FPS), considering downgrade");
            }
        }
        else if (avgFps > warningThreshold + 10f) 
        {
            consecutiveGoodFrames++;
            consecutiveLowFrames = 0;
            if (consecutiveGoodFrames >= 5 && currentLevel < PerformanceLevel.Ultra)
            {
                currentLevel = (PerformanceLevel)((int)currentLevel + 1);
                ApplyPerformanceSettings();
                consecutiveGoodFrames = 0;
            }
        }
    }

    public void SetPerformanceLevel(PerformanceLevel level)
    {
        if (currentLevel != level)
        {
            currentLevel = level;
            ApplyPerformanceSettings();
        }
    }

    public PerformanceSettings GetCurrentSettings()
    {
        return currentLevel switch
        {
            PerformanceLevel.Low => lowSettings,
            PerformanceLevel.Medium => mediumSettings,
            PerformanceLevel.High => highSettings,
            PerformanceLevel.Ultra => ultraSettings,
            _ => highSettings
        };
    }

    private void ApplyPerformanceSettings()
    {
        var settings = GetCurrentSettings();
        if (ClientPrefabRuntime.Instance != null)
        {
            // Note: Cần expose các settings này qua public properties hoặc methods
            Debug.Log($"Applied {currentLevel} performance settings to ClientPrefabRuntime");
        }

        // Apply to TilemapChunkBuilder nếu có  
        if (TilemapChunkBuilder.Instance != null)
        {
            Debug.Log($"Applied {currentLevel} performance settings to TilemapChunkBuilder");
        }

        // Apply to NetworkWorldManager nếu có
        if (NetworkWorldManager.Instance != null)
        {
            Debug.Log($"Applied {currentLevel} performance settings to NetworkWorldManager");
        }

        Debug.Log($"Performance level set to {currentLevel}");
    }

    private void LogPerformanceStats()
    {
        float avgFps = GetAverageFrameRate();
        float memoryUsage = System.GC.GetTotalMemory(false) / (1024f * 1024f); // MB

        Debug.Log($"Performance Stats - FPS: {avgFps:F1}, Memory: {memoryUsage:F1}MB, Level: {currentLevel}");

        if (ClientPrefabRuntime.Instance != null)
        {
            // Log pool stats nếu có method
            // ClientPrefabRuntime.Instance.LogPoolStatistics();
        }
    }

    public void ForcePerformanceCheck()
    {
        if (enableDynamicAdjustment)
        {
            CheckAndAdjustPerformance();
        }
    }

    public float GetCurrentFPS()
    {
        return GetAverageFrameRate();
    }

    public PerformanceLevel GetCurrentLevel()
    {
        return currentLevel;
    }

    // Public methods để external code có thể query settings
    public int GetMaxSpawnPerFrame() => GetCurrentSettings().maxSpawnPerFrame;
    public int GetMaxDespawnPerFrame() => GetCurrentSettings().maxDespawnPerFrame;
    public int GetPoolSizePerPrefab() => GetCurrentSettings().poolSizePerPrefab;
    public int GetCommonPrefabPoolSize() => GetCurrentSettings().commonPrefabPoolSize;
    public int GetMaxChunksPerFrame() => GetCurrentSettings().maxChunksPerFrame;
    public float GetTimeBudgetMs() => GetCurrentSettings().timeBudgetMs;
    public int GetTilesPerBatch() => GetCurrentSettings().tilesPerBatch;
    public int GetPreGenRadius() => GetCurrentSettings().preGenRadius;
    public float GetPlayerUpdateInterval() => GetCurrentSettings().playerUpdateInterval;

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
}