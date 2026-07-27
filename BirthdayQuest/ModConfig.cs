namespace BirthdayQuest
{
    internal sealed class ModConfig
    {
        public bool BirthdayNotification  { get; set; } = true;
        public bool BirthdayQuest  { get; set; } = true;
        public bool LovedGiftsHint { get; set; } = false;
        public bool NpcScheduleHint { get; set; } = false; 
        public bool SkipUnknownNpcs { get; set; } = false;
    }
}
