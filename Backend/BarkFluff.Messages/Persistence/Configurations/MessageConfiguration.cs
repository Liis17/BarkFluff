using BarkFluff.Messages.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BarkFluff.Messages.Persistence.Configurations;

public class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.HasKey(x => x.Id);


        builder.OwnsOne(m => m.Content, contentBuilder =>
        {
            // Настраиваем свойства MessageContent
            contentBuilder.Property(c => c.Text);

            // Настраиваем отношение с MessageAttachment
            contentBuilder.OwnsMany(c => c.Attachments, attachmentBuilder =>
            {
                attachmentBuilder.WithOwner().HasForeignKey("MessageId");
                attachmentBuilder.HasKey(a => a.Id);

                attachmentBuilder.OwnsMany(a => a.ForwardedAttachments, forwardedBuilder =>
                {
                    forwardedBuilder.WithOwner().HasForeignKey("MessageAttachmentId");
                    forwardedBuilder.HasKey(fa => fa.Id);
                });
            });
        });

    }
}