using Persistence.Common.AWS.Builder;
using System.Globalization;
using Xunit;

namespace Common.Tests
{
    public class DynamoDbQueryBuilderTests
    {
        [Fact]
        public void Equality_TranslatesToFilterWithParameter()
        {
            var builder = new DynamoDbQueryBuilder<TestOrder>().WithExpression(x => x.CustomerName == "alice");
            var (_, filter, values, _) = builder.Build();

            Assert.Equal("CustomerName = :param_0", filter);
            Assert.Equal("alice", values[":param_0"].S);
        }

        [Fact]
        public void GreaterThanOrEqual_TranslatesCorrectly()
        {
            var builder = new DynamoDbQueryBuilder<TestOrder>().WithExpression(x => x.Quantity >= 5);
            var (_, filter, values, _) = builder.Build();

            Assert.Equal("Quantity >= :param_0", filter);
            Assert.Equal("5", values[":param_0"].N);
        }

        [Fact]
        public void ReversedOperands_AreOrientedAndOperatorFlipped()
        {
            // "5 <= x.Quantity" is equivalent to "x.Quantity >= 5"
            var builder = new DynamoDbQueryBuilder<TestOrder>().WithExpression(x => 5 <= x.Quantity);
            var (_, filter, values, _) = builder.Build();

            Assert.Equal("Quantity >= :param_0", filter);
            Assert.Equal("5", values[":param_0"].N);
        }

        [Fact]
        public void NullLiteral_TranslatesToAttributeNotExists()
        {
            var builder = new DynamoDbQueryBuilder<TestOrder>().WithExpression(x => x.ShippingAddress == null);
            var (_, filter, _, _) = builder.Build();

            Assert.Equal("attribute_not_exists(ShippingAddress)", filter);
        }

        [Fact]
        public void NullCapturedVariable_TranslatesToAttributeNotExists()
        {
            string? name = null;
            var builder = new DynamoDbQueryBuilder<TestOrder>().WithExpression(x => x.CustomerName == name);
            var (_, filter, _, _) = builder.Build();

            Assert.Equal("attribute_not_exists(CustomerName)", filter);
        }

        [Fact]
        public void NotEqualNull_TranslatesToAttributeExists()
        {
            var builder = new DynamoDbQueryBuilder<TestOrder>().WithExpression(x => x.ShippingAddress != null);
            var (_, filter, _, _) = builder.Build();

            Assert.Equal("attribute_exists(ShippingAddress)", filter);
        }

        [Fact]
        public void AndAlso_CombinesConditions()
        {
            var builder = new DynamoDbQueryBuilder<TestOrder>()
                .WithExpression(x => x.Quantity > 1 && x.CustomerName == "bob");
            var (_, filter, values, _) = builder.Build();

            Assert.Equal("(Quantity > :param_0 AND CustomerName = :param_1)", filter);
            Assert.Equal("1", values[":param_0"].N);
            Assert.Equal("bob", values[":param_1"].S);
        }

        [Fact]
        public void OrElse_CombinesConditions()
        {
            var builder = new DynamoDbQueryBuilder<TestOrder>()
                .WithExpression(x => x.Quantity > 10 || x.IsExpress);
            var (_, filter, _, _) = builder.Build();

            Assert.Contains("OR", filter);
            Assert.Contains("Quantity > :param_0", filter);
        }

        [Fact]
        public void StartsWith_TranslatesToBeginsWith()
        {
            var builder = new DynamoDbQueryBuilder<TestOrder>().WithExpression(x => x.CustomerName.StartsWith("al"));
            var (_, filter, values, _) = builder.Build();

            Assert.Equal("begins_with(CustomerName, :param_0)", filter);
            Assert.Equal("al", values[":param_0"].S);
        }

        [Fact]
        public void StringContains_TranslatesToContains()
        {
            var builder = new DynamoDbQueryBuilder<TestOrder>().WithExpression(x => x.CustomerName.Contains("li"));
            var (_, filter, values, _) = builder.Build();

            Assert.Equal("contains(CustomerName, :param_0)", filter);
            Assert.Equal("li", values[":param_0"].S);
        }

        [Fact]
        public void CapturedListContains_TranslatesToIn()
        {
            var names = new List<string> { "alice", "bob" };
            var builder = new DynamoDbQueryBuilder<TestOrder>().WithExpression(x => names.Contains(x.CustomerName));
            var (_, filter, values, _) = builder.Build();

            Assert.Equal("CustomerName IN (:param_0,:param_1)", filter);
            Assert.Equal("alice", values[":param_0"].S);
            Assert.Equal("bob", values[":param_1"].S);
        }

        [Fact]
        public void CollectionAny_RegistersZeroParameter()
        {
            var builder = new DynamoDbQueryBuilder<TestOrder>().WithExpression(x => x.Tags.Any());
            var (_, filter, values, _) = builder.Build();

            Assert.Contains("size(Tags) > :zero", filter);
            // Regression: ":zero" must be registered or DynamoDB rejects the expression.
            Assert.True(values.ContainsKey(":zero"));
            Assert.Equal("0", values[":zero"].N);
        }

        [Fact]
        public void ReservedWordProperty_GetsAliased()
        {
            var builder = new DynamoDbQueryBuilder<TestOrder>().WithExpression(x => x.Status == OrderStatus.Shipped);
            var (_, filter, _, aliases) = builder.Build();

            // "Status" is a DynamoDB reserved word and must be replaced by a #alias.
            Assert.DoesNotContain("Status =", filter);
            Assert.Contains(aliases, kvp => kvp.Value == "Status" && filter.Contains(kvp.Key));
        }

        [Fact]
        public void DoubleValues_UseInvariantCulture()
        {
            var originalCulture = CultureInfo.CurrentCulture;
            try
            {
                // de-DE writes 1.5 as "1,5" - DynamoDB requires "1.5".
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");

                var builder = new DynamoDbQueryBuilder<TestOrder>().WithExpression(x => x.Price > 1.5);
                var (_, _, values, _) = builder.Build();

                Assert.Equal("1.5", values[":param_0"].N);
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
            }
        }

        [Fact]
        public void ComparingTwoEntityProperties_Throws()
        {
            Assert.Throws<NotSupportedException>(() =>
                new DynamoDbQueryBuilder<TestOrder>().WithExpression(x => x.Quantity > x.Price));
        }
    }
}
