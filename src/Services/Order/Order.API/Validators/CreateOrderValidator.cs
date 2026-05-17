using FluentValidation;
using Order.API.Dtos;
using Order.Domain.Enums;

namespace Order.API.Validators;

public class CreateOrderValidator : AbstractValidator<CreateOrderRequest>
{
    public CreateOrderValidator()
    {
        RuleFor(x => x.ReceiverName)
            .NotEmpty().WithMessage("Tên người nhận không được để trống.")
            .MaximumLength(100).WithMessage("Tên người nhận không được quá 100 ký tự.");

        RuleFor(x => x.PhoneNumber)
            .NotEmpty().WithMessage("Số điện thoại không được để trống.")
            .Matches(@"^0\d{9}$").WithMessage("Số điện thoại phải gồm 10 chữ số, bắt đầu bằng 0.");

        RuleFor(x => x.ShippingAddress)
            .NotEmpty().WithMessage("Địa chỉ giao hàng không được để trống.")
            .MaximumLength(500).WithMessage("Địa chỉ giao hàng không được quá 500 ký tự.");

        RuleFor(x => x.PaymentMethod)
            .NotEmpty().WithMessage("Phương thức thanh toán không được để trống.")
            .Must(pm => Enum.TryParse<PaymentMethodType>(pm, true, out _))
            .WithMessage("Phương thức thanh toán không hợp lệ. Chấp nhận: COD, CreditCard, PayPal.");

        RuleFor(x => x.Items)
            .NotEmpty().WithMessage("Danh sách sản phẩm không được rỗng.");

        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId)
                .NotEmpty().WithMessage("ProductId không được để trống.");
            item.RuleFor(i => i.Quantity)
                .GreaterThan(0).WithMessage("Số lượng của mỗi sản phẩm phải lớn hơn 0.");
        });
    }
}
