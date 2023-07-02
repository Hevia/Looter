using RandomlyGeneratedItems.Tiers;

namespace RandomlyGeneratedItems
{
    public sealed class RandItemContainer
    {
        public NewtLoot RandItemTier { get; private set; }
        public static int EnumVal = 42;
        public static RandItemContainer instance = null;
        private static readonly object padlock = new object();

        RandItemContainer()
        {
            this.RandItemTier = new NewtLoot();
        }

        public static RandItemContainer Instance
        {
            get
            {
                lock (padlock)
                {
                    if (instance == null)
                    {
                        instance = new RandItemContainer();
                    }
                    return instance;
                }
            }
        }
    }
}
