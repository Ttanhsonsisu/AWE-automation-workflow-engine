using System;
using System.Collections.Generic;
using System.Text;
using AWE.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AWE.Infrastructure.Persistence.Configurations;

public class ApprovalTokenConfiguration : IEntityTypeConfiguration<ApprovalToken>
{
    public void Configure(EntityTypeBuilder<ApprovalToken> builder)
    {
        builder.ToTable("ApprovalToken");

        // Primary Key
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .ValueGeneratedOnAdd();
    }
}
