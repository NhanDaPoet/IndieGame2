using Mirror;
using UnityEngine;

[DisallowMultipleComponent]
public class PrefabIdHolder : NetworkBehaviour
{
    [SyncVar]
    public ushort PrefabId;
}
