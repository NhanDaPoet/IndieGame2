using UnityEngine;

public class EnemyAudioManager : MonoBehaviour
{
    [SerializeField] private AudioClip[] slamSounds;
    [SerializeField] private AudioClip[] rainSounds;
    [SerializeField] private AudioClip[] impactSounds;

    private AudioSource audioSource;

    private void Start()
    {
        audioSource = GetComponent<AudioSource>();
    }

    public void PlaySlamSound()
    {
        PlayRandomSound(slamSounds);
    }

    public void PlayRainSound()
    {
        PlayRandomSound(rainSounds);
    }

    public void PlayImpactSound()
    {
        PlayRandomSound(impactSounds);
    }

    private void PlayRandomSound(AudioClip[] clips)
    {
        if (clips.Length > 0 && audioSource != null)
        {
            AudioClip clip = clips[Random.Range(0, clips.Length)];
            audioSource.PlayOneShot(clip);
        }
    }
}
