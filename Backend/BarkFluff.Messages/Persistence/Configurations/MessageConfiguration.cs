using BarkFluff.Messages.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BarkFluff.Messages.Persistence.Configurations;

public class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(m => m.IsDeleted).HasDefaultValue(false);
        builder.Property(m => m.IsEdited).HasDefaultValue(false);

        // Все выборки сообщений фильтруют по ChatId и сортируют по SentAt
        // (история чата, последнее сообщение, счётчик непрочитанных).
        // Без этого индекса PostgreSQL делает sequential scan всей таблицы Messages.
        builder.HasIndex(m => new { m.ChatId, m.SentAt });

        // Идемпотентность будущего импорта федеративных сообщений (Фаза 2):
        // один и тот же FederatedId не может встретиться дважды в одном чате.
        builder.HasIndex(m => new { m.ChatId, m.FederatedId })
            .IsUnique()
            .HasFilter("\"FederatedId\" IS NOT NULL");

        builder.OwnsOne(m => m.Content, contentBuilder =>
        {
            // Настраиваем свойства MessageContent
            contentBuilder.Property(c => c.Text).HasMaxLength(4096);

            // Настраиваем отношение с MessageAttachment
            contentBuilder.OwnsMany(c => c.Attachments, attachmentBuilder =>
            {
                attachmentBuilder.WithOwner().HasForeignKey("MessageId");
                attachmentBuilder.HasKey(a => a.Id);

                // Проверки доступа к fed-файлу (этапы 3.2/3.3) ищут вложение по FileId —
                // без индекса это seq scan по всем вложениям ноды на каждое скачивание.
                attachmentBuilder.HasIndex(a => a.FileId);

                attachmentBuilder.OwnsMany(a => a.ForwardedAttachments, forwardedBuilder =>
                {
                    forwardedBuilder.WithOwner().HasForeignKey("MessageAttachmentId");
                    forwardedBuilder.HasKey(fa => fa.Id);
                });
            });
        });

    }
}