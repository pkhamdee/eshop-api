using Eshop.Domain.Applications;
using Eshop.Domain.Auctions;
using Eshop.Domain.Branches;
using Eshop.Domain.Catalog;
using Eshop.Domain.Contracts;
using Eshop.Domain.Customers;
using Eshop.Domain.Handovers;
using Eshop.Domain.Offers;
using Eshop.Domain.Staff;
using Microsoft.EntityFrameworkCore;

namespace Eshop.Data;

public class EshopDbContext(DbContextOptions<EshopDbContext> options) : DbContext(options)
{
    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<Branch> Branches => Set<Branch>();

    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<SmsCode> SmsCodes => Set<SmsCode>();

    public DbSet<Application> Applications => Set<Application>();

    public DbSet<StaffMember> StaffMembers => Set<StaffMember>();

    public DbSet<Offer> Offers => Set<Offer>();

    public DbSet<Contract> Contracts => Set<Contract>();

    public DbSet<ContractNumberCounter> ContractNumberCounters => Set<ContractNumberCounter>();

    /// <summary>Contracts queued for eshop-sync-master to push to the Enterprise System (ADR-0001).</summary>
    public DbSet<ContractOutboxEntry> ContractOutbox => Set<ContractOutboxEntry>();

    /// <summary>The Handover of each Contract's Product: eShop's operational record beside the Contract.</summary>
    public DbSet<Handover> Handovers => Set<Handover>();

    /// <summary>Payments pulled from the Enterprise System by sync-master (#10).</summary>
    public DbSet<Payment> Payments => Set<Payment>();

    /// <summary>sync-master's pull position per Enterprise table.</summary>
    public DbSet<SyncWatermark> SyncWatermarks => Set<SyncWatermark>();

    /// <summary>Submissions that failed Eligibility after ID Verification (under 20): never Applications.</summary>
    public DbSet<EligibilityRefusal> EligibilityRefusals => Set<EligibilityRefusal>();

    public DbSet<Auction> Auctions => Set<Auction>();

    public DbSet<Lot> Lots => Set<Lot>();

    public DbSet<Bid> Bids => Set<Bid>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(EshopDbContext).Assembly);
}
