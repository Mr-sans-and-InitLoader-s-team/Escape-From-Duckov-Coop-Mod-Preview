using UnityEngine;

namespace EscapeFromDuckovCoopMod;

public sealed class MountedRiderLock : MonoBehaviour
{
    public string PlayerId;
    public CharacterMainControl Rider;
    public CharacterMainControl Vehicle;

    public void Bind(string playerId, CharacterMainControl rider, CharacterMainControl vehicle)
    {
        PlayerId = playerId;
        Rider = rider;
        Vehicle = vehicle;
        enabled = true;
    }

    public void Unbind()
    {
        PlayerId = null;
        Vehicle = null;
        enabled = false;
    }

    private void LateUpdate()
    {
        if (!enabled || !Rider || !Vehicle)
            return;

        var socket = Vehicle.VehicleSocket;
        if (socket)
        {
            Rider.transform.position = socket.position;
            if (Rider.modelRoot)
                Rider.modelRoot.transform.rotation = socket.rotation;
        }
        else
        {
            Rider.transform.position = Vehicle.transform.position;
            if (Rider.modelRoot)
                Rider.modelRoot.transform.rotation = Vehicle.transform.rotation;
        }

        Rider.ridingVehicleType = Vehicle.vehicleAnimationType;
        if (Rider.movementControl != null)
            Rider.movementControl.MovementEnabled = false;
    }
}
