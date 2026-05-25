using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Identity.Domain.Models;
using Identity.Infrastructure.Data;
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
        IdentityAppDbContext dbContext,
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

        var random = new Random();
        int seededCount = 0;
        int maxAttempts = totalToSeed * 10;
        int attempts = 0;

        while (seededCount < totalToSeed && attempts < maxAttempts)
        {
            attempts++;

            // Create a logical name
            string lastName = LastNames[random.Next(LastNames.Length)];
            string middleName = MiddleNames[random.Next(MiddleNames.Length)];
            string firstName = FirstNames[random.Next(FirstNames.Length)];
            string fullName = $"{lastName} {middleName} {firstName}";

            // Create a matching email
            string rawEmailPrefix = RemoveDiacritics($"{firstName.ToLower()}{middleName.ToLower()}{lastName.ToLower()}{random.Next(100, 9999)}");
            string domain = EmailDomains[random.Next(EmailDomains.Length)];
            string email = $"{rawEmailPrefix}@{domain}";

            if (existingEmailsSet.Contains(email))
            {
                continue;
            }

            // Create a phone number in Vietnamese format
            string[] prefixes = ["090", "091", "098", "097", "035", "038", "086", "077", "093", "094"];
            string prefix = prefixes[random.Next(prefixes.Length)];
            string phoneSuffix = random.Next(1000000, 9999999).ToString();
            string phone = $"{prefix}{phoneSuffix}";

            if (existingPhonesSet.Contains(phone))
            {
                continue;
            }

            // Create a logical address
            int num = random.Next(1, 450);
            string street = Streets[random.Next(Streets.Length)];
            string district = Districts[random.Next(Districts.Length)];
            string city = Cities[random.Next(Cities.Length)];
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
