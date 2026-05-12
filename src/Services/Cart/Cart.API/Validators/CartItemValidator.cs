using Cart.API.Dtos;
using FluentValidation;

namespace Cart.API.Validators;

public class CartItemValidator : AbstractValidator<CartItemRequest>
{
    public CartItemValidator()
    {
        RuleFor(x => x.ProductId)
            .NotEmpty().WithMessage("ProductId không được để trống.");

        RuleFor(x => x.Quantity)
            .GreaterThan(0).WithMessage("Số lượng phải lớn hơn 0.");
    }
}
