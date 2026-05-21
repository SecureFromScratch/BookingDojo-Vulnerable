namespace BookingDojo.Api.Tests.Infrastructure;

// In the vulnerable-clean branch there is only one mode (vulnerable).
// All workshop factories are simply aliases for CustomWebApplicationFactory.

public class VulnerableWorkshopFactory : CustomWebApplicationFactory { }

// SQLi and resource consumption are both always vulnerable in this branch.
public class ResourceConsumptionVulnerableFactory : CustomWebApplicationFactory { }

public class VulnerableCouponCartFactory : CustomWebApplicationFactory { }

public class FixedCouponCartFactory : CustomWebApplicationFactory { }

public class FixedWorkshopFactory : CustomWebApplicationFactory { }
