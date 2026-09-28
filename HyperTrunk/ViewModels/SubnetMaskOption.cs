namespace HyperTrunk.ViewModels
{
    public class SubnetMaskOption
    {
        public string Display { get; }
        public string Mask { get; }

        public SubnetMaskOption(string display, string mask)
        {
            Display = display;
            Mask = mask;
        }
    }
}
