using MediatR;
using PersonalAIAssistant.Memory.Business.Commands;
using PersonalAIAssistant.Memory.Core.Domains;
using PersonalAIAssistant.Memory.Core.Domains.Enums;
using PersonalAIAssistant.Memory.Core.Exceptions;
using PersonalAIAssistant.Memory.Core.Interfaces.Others;
using PersonalAIAssistant.Memory.Infrastructure.Mongo;

namespace PersonalAIAssistant.Memory.Business.Handlers
{
    public class AddMemoryCommandHandler : IRequestHandler<AddMemoryCommand, Guid>
    {
        private readonly IEventStore _eventStore;
        private readonly IEventBus _eventBus;

        public AddMemoryCommandHandler(IEventStore eventStore, IEventBus eventBus)
        {
            _eventStore = eventStore;
            _eventBus = eventBus;
        }

        public async Task<Guid> Handle(AddMemoryCommand request, CancellationToken cancellationToken)
        {
            var aggregate = new MemoryAggregate();

            if (!Enum.TryParse<MemorySource>(request.Source, ignoreCase: true, out var source) ||
                !Enum.IsDefined(source))
            {
                throw new DomainException($"Unsupported memory source '{request.Source}'.");
            }

            aggregate.AddMemory(
                      rawText: request.RawText,
                      source: source,
                      tags: request.Tags,
                      userId: request.UserId,
                      correlationId: request.CorrelationId
                      );

            var uncommittedEvents = aggregate.UncommittedEvents.ToList();

            if (!uncommittedEvents.Any())
            {
                return aggregate.Id.Value;
            }

            var streamId = $"memory-{aggregate.Id.Value}";

            await _eventStore.AppendEventsAsync(streamId, uncommittedEvents, 0, cancellationToken);

            foreach (var evt in uncommittedEvents)
            {
                await _eventBus.PublishAsync(evt, cancellationToken);
            }

            aggregate.ClearUncommittedEvents();

            return aggregate.Id.Value;
        }
    }
}
