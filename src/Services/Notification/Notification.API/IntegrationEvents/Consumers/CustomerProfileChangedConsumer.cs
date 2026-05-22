using EventBus.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Notification.Domain.Entities;
using Notification.Infrastructure.Data;

namespace Notification.API.IntegrationEvents.Consumers;

public class CustomerProfileChangedConsumer(
    NotificationDbContext dbContext,
    ILogger<CustomerProfileChangedConsumer> logger)
    : IConsumer<CustomerProfileChangedEvent>
{
    public async Task Consume(ConsumeContext<CustomerProfileChangedEvent> context)
    {
        var message = context.Message;

        try
        {
            var recipient = await dbContext.Recipients
                .FirstOrDefaultAsync(r => r.CustomerId == message.CustomerId, context.CancellationToken);

            if (recipient is null)
            {
                recipient = NotificationRecipient.Create(
                    message.CustomerId,
                    message.Email,
                    message.PhoneNumber,
                    message.FullName,
                    message.OccurredAt);

                dbContext.Recipients.Add(recipient);
            }
            else
            {
                recipient.UpdateContact(
                    message.Email,
                    message.PhoneNumber,
                    message.FullName,
                    message.OccurredAt);
            }

            await dbContext.SaveChangesAsync(context.CancellationToken);

            logger.LogInformation(
                "Synced notification recipient {CustomerId} ({Email}).",
                recipient.CustomerId,
                recipient.Email);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to sync notification recipient for customer {CustomerId}.",
                message.CustomerId);
            throw;
        }
    }
}
