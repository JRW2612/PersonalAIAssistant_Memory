using FluentValidation;
using MediatR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PersonalAIAssistant.Memory.Business.Behaviors
{
    public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
    {
        private readonly IEnumerable<IValidator<TRequest>> _validators;
        public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
        {
            _validators = validators;
        }
        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            if(_validators.Any())
            {
                var context = new ValidationContext<TRequest>(request);

                var validationResults = (await Task.WhenAll(_validators.Select
                    (vr => vr.ValidateAsync(context, cancellationToken))))
                    .SelectMany(err => err.Errors)
                    .Where(f => f != null)
                    .ToList();

                if (validationResults.Any())
                {
                    throw new ValidationException(validationResults);
                }
            }
          
            return await next(cancellationToken);
        }
    }
}
