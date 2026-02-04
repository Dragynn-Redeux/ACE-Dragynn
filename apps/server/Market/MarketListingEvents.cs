using System;

namespace ACE.Server.Market;

public static class MarketListingEvents
{
    public static event Action<int>? ListingCreated;

    public static void RaiseListingCreated(int listingId)
    {
        if (listingId <= 0)
        {
            return;
        }

        try
        {
            ListingCreated?.Invoke(listingId);
        }
        catch
        {
            // event handlers should never break core server functionality
        }
    }
}
