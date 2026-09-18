using LoRClone.Model;

namespace LoRClone.Controller
{
    public static class InputMode
    {
        public static bool Hotseat = false;
        // Phe mà input local sẽ điều khiển. Hotseat: luôn = phe đang có priority.
        public static bool ActingSide(GameModel m) => Hotseat ? m.isPlayerPriority : true;
    }
}