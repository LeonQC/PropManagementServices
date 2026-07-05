using AuthService.Business.DTOs;
using AuthService.Models;
using FluentValidation;

namespace AuthService.Business.Validators;

public class RegisterDtoValidator : AbstractValidator<RegisterDto>
{
    public RegisterDtoValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8);
        RuleFor(x => x.Role).NotEmpty()
            .Must(r => Roles.All.Contains(r))
            .WithMessage(x => $"'{x.Role}' is not a valid role. Allowed: {string.Join(", ", Roles.All)}.");
    }
}

public class UpdateProfileDtoValidator : AbstractValidator<UpdateProfileDto>
{
    public UpdateProfileDtoValidator()
    {
        RuleFor(x => x.FullName).MaximumLength(200).When(x => x.FullName is not null);
        RuleFor(x => x.AvatarUrl).MaximumLength(2048).When(x => x.AvatarUrl is not null);
    }
}

public class ChangeRoleDtoValidator : AbstractValidator<ChangeRoleDto>
{
    public ChangeRoleDtoValidator()
    {
        RuleFor(x => x.Role).NotEmpty()
            .Must(r => Roles.All.Contains(r))
            .WithMessage(x => $"'{x.Role}' is not a valid role. Allowed: {string.Join(", ", Roles.All)}.");
    }
}
