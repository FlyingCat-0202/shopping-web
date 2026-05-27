using System.Text;
using Bogus;
using Identity.Domain.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DbSeeder;

public static class CustomerGenerator
{
    private static readonly string[] LastNames = ["Nguyễn", "Trần", "Lê", "Phạm", "Hoàng", "Huỳnh", "Phan", "Vũ", "Võ", "Đặng", "Bùi", "Đỗ", "Hồ", "Ngô", "Dương", "Lý"];
    private static readonly string[] MiddleNames = ["Văn", "Thị", "Minh", "Anh", "Đức", "Huy", "Hoài", "Quang", "Thanh", "Ngọc", "Hồng", "Xuân", "Hữu", "Tấn", "Quốc", "Tuấn"];
    private static readonly string[] FirstNames = ["Nam", "Hùng", "Trang", "Linh", "Phương", "Anh", "Đức", "Kiên", "Duy", "Sơn", "Hải", "Tùng", "Thảo", "Vy", "Hà", "Lan", "Hương", "Quỳnh", "Hoàng", "Long"];

    private static readonly string[] Cities = ["Hà Nội", "TP. Hồ Chí Minh", "Đà Nẵng", "Cần Thơ", "Hải Phòng", "Nha Trang", "Đà Lạt", "Vũng Tàu", "Vinh", "Buôn Ma Thuột"];
    private static readonly string[] Streets = ["Lê Lợi", "Nguyễn Trãi", "Trần Hưng Đạo", "Chùa Bộc", "Nguyễn Văn Linh", "Hùng Vương", "Quang Trung", "Phan Đình Phùng", "Ngô Quyền", "Hai Bà Trưng"];
    private static readonly string[] Districts = ["Quận 1", "Quận 3", "Đống Đa", "Hai Bà Trưng", "Hải Châu", "Ninh Kiều", "Hồng Bàng", "Vũng Tàu", "Vinh", "Buôn Ma Thuột"];

    private static readonly string[] EmailDomains = ["gmail.com", "yahoo.com", "outlook.com", "shopping.local"];

    public static async Task GenerateCustomersAsync(
        UserManager<Customer> userManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        int totalToSeed)
    {
        Console.WriteLine($"Starting seed of {totalToSeed} customers...");

        const string customerRole = "Customer";
        if (!await roleManager.RoleExistsAsync(customerRole))
        {
            await roleManager.CreateAsync(new IdentityRole<Guid>(customerRole));
        }

        var existingEmails = await userManager.Users
            .Select(u => u.Email)
            .ToListAsync();
        var existingEmailsSet = new HashSet<string>(existingEmails.Where(e => e != null)!, StringComparer.OrdinalIgnoreCase);

        var existingPhones = await userManager.Users
            .Select(u => u.PhoneNumber)
            .ToListAsync();
        var existingPhonesSet = new HashSet<string>(existingPhones.Where(p => p != null)!, StringComparer.OrdinalIgnoreCase);

        var faker = new Faker("vi");
        int seededCount = 0;
        int maxAttempts = totalToSeed * 10;
        int attempts = 0;

        while (seededCount < totalToSeed && attempts < maxAttempts)
        {
            attempts++;

            string lastName = faker.PickRandom(LastNames);
            string middleName = faker.PickRandom(MiddleNames);
            string firstName = faker.Name.FirstName();
            string fullName = $"{lastName} {middleName} {firstName}";

            string rawEmailPrefix = RemoveDiacritics($"{firstName}.{middleName}.{lastName}".ToLowerInvariant())
                .Replace(" ", "")
                .Replace("..", ".");
            string domain = faker.PickRandom(EmailDomains);
            string emailSuffix = faker.Random.Number(1_000, 999_999).ToString();
            string email = $"{rawEmailPrefix}{emailSuffix}@{domain}";

            if (existingEmailsSet.Contains(email))
            {
                continue;
            }

            string[] prefixes = ["090", "091", "098", "097", "035", "038", "086", "077", "093", "094"];
            string prefix = faker.PickRandom(prefixes);
            string phoneSuffix = faker.Random.Number(1_000_000, 9_999_999).ToString();
            string phone = $"{prefix}{phoneSuffix}";

            if (existingPhonesSet.Contains(phone))
            {
                continue;
            }

            int num = faker.Random.Number(1, 450);
            string street = faker.PickRandom(Streets);
            string district = faker.PickRandom(Districts);
            string city = faker.PickRandom(Cities);
            string address = $"{num} Đường {street}, {district}, {city}";

            var customer = new Customer
            {
                UserName = email,
                Email = email,
                FullName = fullName,
                PhoneNumber = phone,
                Address = address
            };

            var result = await userManager.CreateAsync(customer, "Customer123");
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(customer, customerRole);
                existingEmailsSet.Add(email);
                existingPhonesSet.Add(phone);
                seededCount++;

                if (seededCount % 50 == 0 || seededCount == totalToSeed)
                {
                    Console.WriteLine($"Seeded {seededCount}/{totalToSeed} customers...");
                }
            }
            else
            {
                var errors = string.Join("; ", result.Errors.Select(e => e.Description));
                Console.WriteLine($"Failed to create user {email}: {errors}");
            }
        }

        Console.WriteLine($"Successfully seeded {seededCount} customers.");
    }

    private static string RemoveDiacritics(string text)
    {
        var normalizedString = text.Normalize(NormalizationForm.FormD);
        var stringBuilder = new StringBuilder();

        foreach (var c in normalizedString)
        {
            var unicodeCategory = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                if (c == 'đ' || c == 'Đ')
                {
                    stringBuilder.Append('d');
                }
                else
                {
                    stringBuilder.Append(c);
                }
            }
        }

        return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
    }
}
