using Amazon.DynamoDBv2.Model;
using Domain.Common.Entities;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Text;
using System.Text.RegularExpressions;

namespace Persistence.Common.AWS.Builder
{
    public class DynamoDbQueryBuilder<T> where T : DocEntity
    {
        private readonly StringBuilder _filterExpressionBuilder = new StringBuilder();
        private readonly Dictionary<string, AttributeValue> _expressionAttributeValues = new Dictionary<string, AttributeValue>();
        private readonly Dictionary<string, string> _aliases = new Dictionary<string, string>();
        private int _parameterCounter;

        private static readonly IDynamoDBEntityBuilder? _entityConfiguration = InMemoryDynamoDBEntitiesConfiguration.GetConfiguration<T>();


        [SuppressMessage("Maintainability", "S3242", Justification = "This function getting us for Bool expression only so don't want move to general type.")]
        public DynamoDbQueryBuilder<T> WithExpression(Expression<Func<T, bool>> expression)
        {
            var filterExpression = ParseExpression(expression.Body);
            _filterExpressionBuilder.Append(filterExpression);
            return this;
        }

        public (KeyFilter? KeyFilterExpression, string FilterExpression,
                Dictionary<string, AttributeValue> ExpressionAttributeValues,
                Dictionary<string, string> Aliases) BuildFromExpression(Expression expression)
        {
            // Reset the internal state
            _filterExpressionBuilder.Clear();
            _expressionAttributeValues.Clear();
            _aliases.Clear();
            _parameterCounter = 0;

            // Parse the provided expression
            var parseExpression = ParseExpression(expression);
            AppendToFilterBuilder(parseExpression);

            var keyProperties = _entityConfiguration?
            .GetPropertyConfigurations()
            .Where(e => e.IsPrimaryKey || e.IsPartitionKey)
            .ToList();

            string filterExpression = _filterExpressionBuilder.ToString();
            KeyFilter? keyFilter = null;

            if (keyProperties is { Count: > 0 })
            {
                filterExpression = ExtractKeyFilters(filterExpression, keyProperties, out keyFilter);
            }

            return (keyFilter, filterExpression, _expressionAttributeValues, _aliases);
        }

        public (KeyFilter? KeyFilterExpression, string FilterExpression,
                Dictionary<string, AttributeValue> ExpressionAttributeValues,
                Dictionary<string, string> Aliases) Build()
        {
            var keyProperties = _entityConfiguration?
            .GetPropertyConfigurations()
            .Where(e => e.IsPrimaryKey || e.IsPartitionKey)
            .ToList();

            string filterExpression = _filterExpressionBuilder.ToString();
            KeyFilter? keyFilter = null;

            if (keyProperties is { Count: > 0 })
            {
                filterExpression = ExtractKeyFilters(filterExpression, keyProperties, out keyFilter);
            }

            return (keyFilter, filterExpression, _expressionAttributeValues, _aliases);
        }

        [SuppressMessage("Maintainability", "S1067", Justification = "Different kind of expression can handle to gather.")]
        private string ParseExpression(Expression expression)
        {
            return expression.NodeType switch
            {
                ExpressionType.Lambda => ParseLambdaExpression((LambdaExpression)expression),
                ExpressionType.AndAlso => ParseLogicalExpression((BinaryExpression)expression, "AND"),
                ExpressionType.OrElse => ParseLogicalExpression((BinaryExpression)expression, "OR"),
                ExpressionType.Equal or
                ExpressionType.NotEqual or
                ExpressionType.GreaterThan or
                ExpressionType.GreaterThanOrEqual or
                ExpressionType.LessThan or
                ExpressionType.LessThanOrEqual => ParseBinaryExpression((BinaryExpression)expression),
                ExpressionType.Call => ParseMethodCallExpression((MethodCallExpression)expression),
                ExpressionType.MemberAccess => ParseMemberAccessExpression((MemberExpression)expression),
                ExpressionType.Not => ParseNotExpression((UnaryExpression)expression),
                // The queryable root (e.g. the DynamoDbSet itself) translates to "no filter".
                ExpressionType.Constant when ((ConstantExpression)expression).Value is IQueryable => string.Empty,
                _ => throw new NotSupportedException($"Operation '{expression.NodeType}' is not supported.")
            };
        }

        private string ParseLambdaExpression(LambdaExpression expression)
        {
            // Lambda expressions are expected to have a body that is a logical or comparison expression.
            return ParseExpression(expression.Body);
        }

        private string ParseLogicalExpression(BinaryExpression expression, string logicalOperator)
        {
            var left = ParseExpression(expression.Left);
            var right = ParseExpression(expression.Right);
            return $"({left} {logicalOperator} {right})";
        }

        private string ParseBinaryExpression(BinaryExpression expression)
        {
            // Orient the operands so the entity member is always on the left. This supports
            // predicates written as "5 == x.Age" by swapping the sides and flipping the operator.
            var memberSide = expression.Left;
            var valueSide = expression.Right;
            var nodeType = expression.NodeType;

            var leftIsMember = IsEntityMember(memberSide);
            var rightIsMember = IsEntityMember(valueSide);

            if (leftIsMember && rightIsMember)
            {
                throw new NotSupportedException("Comparing two entity properties in one predicate is not supported by DynamoDB expressions.");
            }

            if (!leftIsMember && rightIsMember)
            {
                (memberSide, valueSide) = (valueSide, memberSide);
                nodeType = nodeType switch
                {
                    ExpressionType.GreaterThan => ExpressionType.LessThan,
                    ExpressionType.GreaterThanOrEqual => ExpressionType.LessThanOrEqual,
                    ExpressionType.LessThan => ExpressionType.GreaterThan,
                    ExpressionType.LessThanOrEqual => ExpressionType.GreaterThanOrEqual,
                    _ => nodeType
                };
            }

            var left = GetMemberName(memberSide);

            // Null comparison - covers both literal nulls and captured variables that
            // evaluate to null at translation time.
            var value = valueSide is ConstantExpression constantExpression
                ? constantExpression.Value
                : GetValueAllowNull(valueSide);

            if (value is null)
            {
                return nodeType switch
                {
                    ExpressionType.Equal => $"attribute_not_exists({left})",
                    ExpressionType.NotEqual => $"attribute_exists({left})",
                    _ => throw new NotSupportedException($"Binary operator '{nodeType}' with a null operand is not supported.")
                };
            }

            var paramName = $":param_{_parameterCounter++}";
            _expressionAttributeValues[paramName] = ConvertToAttributeValue(value);

            return nodeType switch
            {
                ExpressionType.Equal => $"{left} = {paramName}",
                ExpressionType.NotEqual => $"{left} <> {paramName}",
                ExpressionType.GreaterThan => $"{left} > {paramName}",
                ExpressionType.GreaterThanOrEqual => $"{left} >= {paramName}",
                ExpressionType.LessThan => $"{left} < {paramName}",
                ExpressionType.LessThanOrEqual => $"{left} <= {paramName}",
                _ => throw new NotSupportedException($"Binary operator '{nodeType}' is not supported.")
            };
        }

        /// <summary>
        /// True when the expression is a property/field chain rooted at the lambda parameter
        /// (i.e. an entity attribute rather than a captured value).
        /// </summary>
        private static bool IsEntityMember(Expression expression)
        {
            if (expression is UnaryExpression unaryExpression)
            {
                expression = unaryExpression.Operand;
            }

            while (expression is MemberExpression memberExpression)
            {
                if (memberExpression.Expression is ParameterExpression)
                {
                    return true;
                }

                if (memberExpression.Expression is null)
                {
                    return false;
                }

                expression = memberExpression.Expression;
            }

            return expression is ParameterExpression;
        }

        private static object? GetValueAllowNull(Expression expression)
        {
            if (expression is ConstantExpression constantExpression)
            {
                return constantExpression.Value;
            }

            var objectMember = Expression.Convert(expression, typeof(object));
            var getterLambda = Expression.Lambda<Func<object?>>(objectMember);
            return getterLambda.Compile()();
        }

        private string ParseMethodCallExpression(MethodCallExpression expression)
        {
            var methodName = expression.Method.Name;
            const string errorMessage = "Method call must have an associated instance.";

            switch (methodName)
            {
                case "Where":
                    return ParseWhereMethod(expression);
                case "Any":
                    return ParseAnyMethod(expression);
                case "Contains":
                    // When Contains is called on a external  collection (e.g., a list)
                    if (expression.Object != null &&
                        IsCapturedVariable(expression.Object) &&
                        typeof(IEnumerable).IsAssignableFrom(expression.Object.Type))
                    {
                        return HandleInMethod(expression,
                            GetMemberName(expression.Arguments[0] as MemberExpression ??
                                throw new InvalidOperationException("Expression does not refer to a valid member.")));
                    }
                    else if (expression.Object != null && typeof(IEnumerable).IsAssignableFrom(expression.Object.Type))
                    {
                        return HandleContainsMethod(expression,
                            GetMemberName(expression.Object as MemberExpression ??
                                throw new InvalidOperationException(errorMessage)));
                    }

                    // If neither case matches, throw an error indicating unsupported 'Contains' usage.
                    throw new NotSupportedException($"Method '{methodName}' is not supported.");
                case "StartsWith":
                    return HandleStartsWithMethod(expression,
                    GetMemberName(expression.Object as MemberExpression ??
                     throw new InvalidOperationException(errorMessage)));
                case "In":
                    return HandleInMethod(expression,
                    GetMemberName(expression.Object as MemberExpression ??
                     throw new InvalidOperationException(errorMessage)));
                case "Between":
                    return HandleBetweenMethod(expression,
                    GetMemberName(expression.Object as MemberExpression ??
                    throw new InvalidOperationException(errorMessage)));
                default:
                    throw new NotSupportedException($"Method '{methodName}' is not supported.");
            }
        }

        private string ParseWhereMethod(MethodCallExpression expression)
        {
            if (expression.Arguments[0] is not ConstantExpression)
            {
                var whereExpression = ParseExpression(expression.Arguments[0]);
                AppendToFilterBuilder(whereExpression);
            }

            var lambdaExpression = (LambdaExpression)((UnaryExpression)expression.Arguments[1]).Operand;
            return ParseExpression(lambdaExpression.Body);
        }
        private static bool IsCapturedVariable(Expression expression)
        {
            // Check if the expression is a MemberExpression and its object is a ConstantExpression (closure)
            return expression is MemberExpression memberExpr && memberExpr.Expression is ConstantExpression;
        }

        private string ParseAnyMethod(MethodCallExpression expression)
        {
            var collectionName = GetMemberName(expression.Arguments[0]);

            if (expression.Arguments.Count == 2 && expression.Arguments[1] is LambdaExpression lambdaExpression)
            {
                var conditionExpression = ParseExpression(lambdaExpression.Body);

                throw new NotSupportedException(
                    $"DynamoDB does not support the 'Any' method with a condition inside the collection: {conditionExpression}"
                );
            }

            // ":zero" must be registered in the attribute values or DynamoDB rejects the expression.
            _expressionAttributeValues[":zero"] = new AttributeValue { N = "0" };
            return $"attribute_exists({collectionName}) AND size({collectionName}) > :zero";
        }

        private string HandleContainsMethod(MethodCallExpression expression, string memberName)
        {
            var paramName = $":param_{_parameterCounter++}";
            var argumentValue = GetArgumentValueAsAttributeValue(expression.Arguments[0]);

            _expressionAttributeValues[paramName] = argumentValue;

            return $"contains({memberName}, {paramName})";
        }

        private string HandleStartsWithMethod(MethodCallExpression expression, string memberName)
        {
            var paramName = $":param_{_parameterCounter++}";
            var argumentValue = GetArgumentValueAsAttributeValue(expression.Arguments[0]);

            _expressionAttributeValues[paramName] = argumentValue;

            return $"begins_with({memberName}, {paramName})";
        }

        private string HandleInMethod(MethodCallExpression expression, string memberName)
        {
            if (expression.Object != null && typeof(IEnumerable).IsAssignableFrom(expression.Object.Type))
            {
                var collection = (IEnumerable)GetValue(expression.Object); // Extract the collection
                var values = collection.Cast<object>().ToList();
                var placeholders = new List<string>();

                // Generate parameter names and populate _expressionAttributeValues
                foreach (var value in values)
                {
                    var paramName = $":param_{_parameterCounter++}";
                    _expressionAttributeValues[paramName] = ConvertToAttributeValue(value);
                    placeholders.Add(paramName);
                }

                // Return the 'IN' clause for DynamoDB
                return $"{memberName} IN ({string.Join(",", placeholders)})";
            }
            else
            {
                var values = ((IEnumerable)GetValue(expression.Arguments[1])).Cast<object>().ToList();
                var placeholders = new List<string>();

                foreach (var value in values)
                {
                    var paramName = $":param_{_parameterCounter++}";
                    _expressionAttributeValues[paramName] = ConvertToAttributeValue(value);
                    placeholders.Add(paramName);
                }

                return $"{memberName} IN ({string.Join(",", placeholders)})";
            }
        }

        private string HandleBetweenMethod(MethodCallExpression expression, string memberName)
        {
            var lowerBound = GetValue(expression.Arguments[0]);
            var upperBound = GetValue(expression.Arguments[1]);

            var lowerParamName = $":param_{_parameterCounter++}";
            var upperParamName = $":param_{_parameterCounter++}";

            _expressionAttributeValues[lowerParamName] = ConvertToAttributeValue(lowerBound);
            _expressionAttributeValues[upperParamName] = ConvertToAttributeValue(upperBound);

            return $"{memberName} BETWEEN {lowerParamName} AND {upperParamName}";
        }

        private string ParseMemberAccessExpression(Expression expression)
        {
            var memberName = GetMemberName(expression);

            if (expression.Type == typeof(bool))
            {
                var paramName = $":param_{_parameterCounter++}";
                _expressionAttributeValues[paramName] = new AttributeValue { BOOL = true };
                return $"{memberName} = {paramName}";
            }

            return memberName;
        }

        private string ParseNotExpression(UnaryExpression expression)
        {
            var operand = expression.Operand;

            if (operand is MemberExpression memberExpression && operand.Type == typeof(bool))
            {
                var memberName = GetMemberName(memberExpression);
                var paramName = $":param_{_parameterCounter++}";
                _expressionAttributeValues[paramName] = new AttributeValue { BOOL = false };
                return $"{memberName} = {paramName}";
            }

            return $"NOT ({ParseExpression(operand)})";
        }

        private string GetMemberName(Expression expression)
        {
            if (expression is UnaryExpression unaryExpression)
            {
                expression = unaryExpression.Operand;
            }

            if (expression is not MemberExpression memberExpression)
            {
                throw new InvalidOperationException("Expression does not refer to a property or field.");
            }

            var parentName = GetParentName(memberExpression.Expression);
            var memberName = memberExpression.Member.Name;

            if (_entityConfiguration is null)
            {
                memberName = CheckAndApplyAlias(memberName);
                return FormatMemberName(parentName, memberName);
            }

            var jsonMemberName = GetJsonMemberName(parentName, memberName);
            if (!string.IsNullOrWhiteSpace(jsonMemberName))
            {
                memberName = jsonMemberName;
            }

            memberName = CheckAndApplyAlias(memberName);
            return FormatMemberName(parentName, memberName);
        }

        private string? GetParentName(Expression? parentExpression)
        {
            if (parentExpression != null && parentExpression.NodeType == ExpressionType.MemberAccess)
            {
                return GetMemberName(parentExpression);
            }

            return null;
        }

        private static string? GetJsonMemberName(string? parentName, string memberName)
        {
            if (string.IsNullOrWhiteSpace(parentName))
            {
                return _entityConfiguration?
                    .GetPropertyConfigurations()
                    .Find(e => e.PropertyName == memberName && !string.IsNullOrWhiteSpace(e.JsonPropertyName))?
                    .JsonPropertyName;
            }

            return _entityConfiguration?
                .GetNestedEntityConfigurations().Find(e => e.PropertyName == parentName)?
                .Configurations?.Find(e => e.PropertyName == memberName && !string.IsNullOrWhiteSpace(e.JsonPropertyName))?
                .JsonPropertyName;
        }

        private static string FormatMemberName(string? parentName, string memberName)
        {
            return parentName != null ? $"{parentName}.{memberName}" : memberName;
        }

        private string CheckAndApplyAlias(string memberName)
        {
            // Return the original member name if not a reserved word
            if (!DynamoConfig.IsReservedWord(memberName))
            {
                return memberName;
            }

            // Re-use the alias if this member was already aliased in this query so the
            // same field always maps to the same placeholder.
            foreach (var kvp in _aliases)
            {
                if (string.Equals(kvp.Value, memberName, StringComparison.Ordinal))
                {
                    return kvp.Key;
                }
            }

            var prefix = memberName.Length >= 2 ? memberName[..2] : memberName;
            var alias = $"#{prefix.ToUpper()}";

            while (_aliases.ContainsKey(alias))
            {
                alias += _aliases.Count;
            }

            _aliases[alias] = memberName;
            return alias;
        }

        private static AttributeValue ConvertToAttributeValue(object value)
        {
            // Numeric values must use invariant culture: DynamoDB "N" values require '.' as
            // the decimal separator regardless of the host machine's culture.
            var invariant = System.Globalization.CultureInfo.InvariantCulture;
            return value switch
            {
                string stringValue => new AttributeValue { S = stringValue },
                int intValue => new AttributeValue { N = intValue.ToString(invariant) },
                long longValue => new AttributeValue { N = longValue.ToString(invariant) },
                short shortValue => new AttributeValue { N = shortValue.ToString(invariant) },
                byte byteValue => new AttributeValue { N = byteValue.ToString(invariant) },
                bool boolValue => new AttributeValue { BOOL = boolValue },
                double doubleValue => new AttributeValue { N = doubleValue.ToString(invariant) },
                decimal decimalValue => new AttributeValue { N = decimalValue.ToString(invariant) },
                float floatValue => new AttributeValue { N = floatValue.ToString(invariant) },
                DateTime dateTimeValue => new AttributeValue { S = dateTimeValue.ToString("o") },
                DateTimeOffset dateTimeOffsetValue => new AttributeValue { S = dateTimeOffsetValue.ToString("o") },
                Guid guidValue => new AttributeValue { S = guidValue.ToString() },
                Enum enumValue => new AttributeValue { S = enumValue.ToString() },
                IEnumerable<string> stringListValue => new AttributeValue { SS = stringListValue.ToList() },
                IEnumerable<int> intListValue => new AttributeValue { NS = intListValue.Select(i => i.ToString(invariant)).ToList() },
                IEnumerable<long> longListValue => new AttributeValue { NS = longListValue.Select(i => i.ToString(invariant)).ToList() },
                IEnumerable<double> doubleListValue => new AttributeValue { NS = doubleListValue.Select(d => d.ToString(invariant)).ToList() },
                IEnumerable<decimal> decimalListValue => new AttributeValue { NS = decimalListValue.Select(d => d.ToString(invariant)).ToList() },
                IEnumerable<Guid> guidListValue => new AttributeValue { SS = guidListValue.Select(g => g.ToString()).ToList() },
                IEnumerable<byte> byteListValue => new AttributeValue { BS = byteListValue.Select(b => new System.IO.MemoryStream(new[] { b })).ToList() },
                _ => throw new NotSupportedException($"Unsupported constant type: {value.GetType().Name}")
            };
        }

        private static object GetValue(Expression expression)
        {
            if (expression is ConstantExpression constantExpression)
            {
                return constantExpression.Value ?? throw new InvalidOperationException("The expression evaluated to null.");
            }

            var objectMember = Expression.Convert(expression, typeof(object));
            var getterLambda = Expression.Lambda<Func<object>>(objectMember);
            var getter = getterLambda.Compile();

            return getter();
        }

        private AttributeValue? GetValueOrParameterName(Expression expression, out string? paramName)
        {
            if (expression is MemberExpression memberExpression && memberExpression.Expression is ParameterExpression)
            {
                paramName = null;
                return null;
            }

            var value = GetValue(expression);
            paramName = $":param_{_parameterCounter++}";

            return ConvertToAttributeValue(value);
        }

        private static AttributeValue GetArgumentValueAsAttributeValue(Expression argument)
        {
            if (argument is ConstantExpression constantArg && constantArg.Value is not null)
            {
                return ConvertToAttributeValue(constantArg.Value);
            }
            else
            {
                var value = GetValue(argument);
                return ConvertToAttributeValue(value);
            }
        }

        private void AppendToFilterBuilder(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression))
            {
                return;
            }

            if (_filterExpressionBuilder.Length > 0)
            {
                _filterExpressionBuilder.Append(" AND ");
            }

            _filterExpressionBuilder.Append(expression);
        }

        // Method to extract key filters and return the normal filter
        private string ExtractKeyFilters(string originalFilter, IEnumerable<PropertyConfiguration> keyProperties, out KeyFilter keyFilters)
        {
            // Initialize KeyFilter object
            keyFilters = new KeyFilter();
            var filterExpression = originalFilter;

            // Match the regex to extract conditions
            MatchCollection matches = Regex.Matches(filterExpression, GetFilterPattern(), RegexOptions.None, TimeSpan.FromSeconds(1));
            string[] invalidOperators = new string[] { "IN", "!=", "<>" };
            string? primaryKeyParamName = null;

            foreach (Match match in matches)
            {
                string condition = match.Value;
                var (fieldName, operatorSymbol) = ExtractFieldNameAndOperatorFromMatch(match);

                // Find the corresponding key property (PartitionKey or PrimaryKey)
                var keyProperty = keyProperties.FirstOrDefault(kp => (kp.JsonPropertyName ?? kp.PropertyName) == fieldName);

                if (keyProperty != null)
                {
                    ProcessKeyProperty(keyFilters, keyProperty, condition, operatorSymbol, invalidOperators, ref primaryKeyParamName, ref filterExpression);
                }
            }

            ResolvePartitionKeyFilter(keyProperties, keyFilters, primaryKeyParamName);

            // Clean up the normal filter and ensure parentheses are balanced
            filterExpression = CleanUpFilter(filterExpression);
            return RemoveRedundantParentheses(filterExpression);
        }

        private static string GetFilterPattern()
        {
            return @"\b([a-zA-Z0-9_.]+)\s*=\s*:\w+|" +
                   @"\b([a-zA-Z0-9_.]+)\s*<>\s*:\w+|" +
                   @"\b([a-zA-Z0-9_.]+)\s*(<|>|<=|>=)\s*:\w+|" +
                   @"\b([a-zA-Z0-9_.]+)\s*IN\s*\(\s*(:\w+\s*(,\s*:\w+\s*)*)\)|" +
                   @"\b([a-zA-Z0-9_.]+)\s*BETWEEN\s*:\w+\s*AND\s*:\w+|" +
                   @"contains\([a-zA-Z0-9_.]+,\s*:\w+\)|" +
                   @"begins_with\([a-zA-Z0-9_.]+,\s*:\w+\)|" +
                   @"starts_with\([a-zA-Z0-9_.]+,\s*:\w+\)|" +
                   @"attribute_exists\([a-zA-Z0-9_.]+\)";
        }

        private static void ProcessKeyProperty(KeyFilter keyFilters, PropertyConfiguration keyProperty,
        string condition, string operatorSymbol, string[] invalidOperators, ref string? primaryKeyParamName, ref string filterExpression)
        {
            if (keyProperty.IsPartitionKey)
            {
                ProcessPartitionKey(keyFilters, condition, operatorSymbol);
            }
            else if (keyProperty.IsPrimaryKey)
            {
                ProcessPrimaryKey(keyFilters, keyProperty, condition, operatorSymbol, invalidOperators, ref primaryKeyParamName);
            }

            if (!keyFilters.PartitionKeyFilter.Contains(Constants.DoNotUseKeyExpression, StringComparison.OrdinalIgnoreCase))
            {
                filterExpression = filterExpression.Replace(condition, "", StringComparison.OrdinalIgnoreCase);
            }
        }

        private static void ProcessPartitionKey(KeyFilter keyFilters, string condition, string operatorSymbol)
        {
            keyFilters.PartitionKeyFilter = !string.IsNullOrEmpty(keyFilters.PartitionKeyFilter) || operatorSymbol != "="
                ? Constants.DoNotUseKeyExpression
                : condition;
        }

        private static void ProcessPrimaryKey(KeyFilter keyFilters, PropertyConfiguration keyProperty,
         string condition, string operatorSymbol, string[] invalidOperators, ref string? primaryKeyParamName)
        {
            if (!string.IsNullOrEmpty(keyFilters.PrimaryKeyFilter) || invalidOperators.Contains(operatorSymbol))
            {
                keyFilters.PartitionKeyFilter = Constants.DoNotUseKeyExpression;
            }
            else
            {
                if (operatorSymbol == "=")
                {
                    primaryKeyParamName = ExtractPrimaryKeyParamName(condition, keyProperty, operatorSymbol);
                }
                keyFilters.PrimaryKeyFilter = condition;
            }
        }

        private static string ExtractPrimaryKeyParamName(string condition, PropertyConfiguration keyProperty, string operatorSymbol)
        {
            string fieldName = keyProperty.JsonPropertyName ?? keyProperty.PropertyName;
            return condition.Replace(operatorSymbol, "", StringComparison.OrdinalIgnoreCase)
                    .Replace(fieldName, "", StringComparison.OrdinalIgnoreCase).Trim();
        }

        private void ResolvePartitionKeyFilter(IEnumerable<PropertyConfiguration> keyProperties, KeyFilter keyFilters, string? primaryKeyParamName)
        {
            if (string.IsNullOrWhiteSpace(keyFilters.PartitionKeyFilter) &&
                !string.IsNullOrWhiteSpace(keyFilters.PrimaryKeyFilter) &&
                !string.IsNullOrWhiteSpace(primaryKeyParamName) &&
                _expressionAttributeValues.TryGetValue(primaryKeyParamName, out var value))
            {
                var primaryKeyValue = value.S;
                var partitionKeyFieldName = keyProperties.FirstOrDefault(e => e.IsPartitionKey)?.JsonPropertyName ??
                                            keyProperties.FirstOrDefault(e => e.IsPartitionKey)?.PropertyName;
                if (!string.IsNullOrWhiteSpace(partitionKeyFieldName) && !string.IsNullOrWhiteSpace(primaryKeyValue))
                {
                    var partitionKeyKeyValue = DynamoUtils.ResolvePartitionKey(primaryKeyValue);
                    var paramName = $":param_{_parameterCounter++}";
                    keyFilters.PartitionKeyFilter = $"{partitionKeyFieldName} = {paramName}";
                    _expressionAttributeValues[paramName] = ConvertToAttributeValue(partitionKeyKeyValue);
                }
            }
        }

        private static (string fieldName, string operatorSymbol) ExtractFieldNameAndOperatorFromMatch(Capture match)
        {
            string matchValue = match.Value.Trim();

            // Handle cases like "contains", "begins_with", "attribute_exists"
            if (matchValue.StartsWith("contains", StringComparison.OrdinalIgnoreCase)
            || matchValue.StartsWith("begins_with", StringComparison.OrdinalIgnoreCase)
            || matchValue.StartsWith("attribute_exists", StringComparison.OrdinalIgnoreCase))
            {
                int openParenIndex = matchValue.IndexOf('(');
                int commaIndex = matchValue.IndexOf(',');
                if (openParenIndex > 0 && commaIndex > openParenIndex)
                {
                    return (matchValue.Substring(openParenIndex + 1, commaIndex - openParenIndex - 1).Trim(), "function");
                }
                else if (matchValue.StartsWith("attribute_exists", StringComparison.OrdinalIgnoreCase))
                {
                    int closeParenIndex = matchValue.IndexOf(')');
                    if (openParenIndex > 0 && closeParenIndex > openParenIndex)
                    {
                        return (matchValue.Substring(openParenIndex + 1, closeParenIndex - openParenIndex - 1).Trim(), "attribute_exists");
                    }
                }
            }

            // Handle IN clauses like "field IN (:param_X, :param_Y)"
            if (matchValue.Contains(" IN ", StringComparison.OrdinalIgnoreCase))
            {
                int inIndex = matchValue.IndexOf(" IN ", StringComparison.Ordinal);
                return (matchValue.Substring(0, inIndex).Trim(), "IN");
            }

            // Handle BETWEEN clauses like "field BETWEEN :param_X AND :param_Y"
            if (matchValue.Contains("BETWEEN", StringComparison.OrdinalIgnoreCase))
            {
                int betweenIndex = matchValue.IndexOf("BETWEEN", StringComparison.Ordinal);
                return (matchValue.Substring(0, betweenIndex).Trim(), "BETWEEN");
            }

            // Handle basic conditions like "field = :param_X", "field != :param_X" or "field <> :param_X".
            // IMPORTANT: multi-character operators must be tested before their single-character
            // prefixes ("<=" before "<", ">=" before ">", and both before "=") or e.g.
            // "field >= :p" would split on "=" and yield field name "field >".
            string[] delimiters = new[] { "!=", "<>", ">=", "<=", ">", "<", "=" };
            foreach (var delimiter in delimiters)
            {
                if (matchValue.Contains(delimiter, StringComparison.OrdinalIgnoreCase))
                {
                    var parts = matchValue.Split(new[] { delimiter }, 2, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 0)
                    {
                        return (parts[0].Trim(), delimiter);  // Return the field name and operator
                    }
                }
            }

            return (string.Empty, string.Empty);
        }

        // Method to clean up operators and extra parentheses
        private static string CleanUpFilter(string filter)
        {
            // 1. Remove empty parentheses caused by removing conditions, like ()
            filter = Regex.Replace(filter, @"\(\s*\)", "", RegexOptions.None, TimeSpan.FromSeconds(1));

            // 2. Remove invalid expressions like "( AND )" or "( OR )"
            filter = Regex.Replace(filter, @"\(\s*(AND|OR)\s*\)", "", RegexOptions.None, TimeSpan.FromSeconds(1));  // Remove ( AND ) or ( OR )

            // 3. Handle redundant logical operators (e.g., AND AND, OR OR)
            filter = Regex.Replace(filter, @"\s*(AND|OR)\s*(AND|OR)\s*", " $1 ", RegexOptions.None, TimeSpan.FromSeconds(1));

            // 4. Remove dangling logical operators (e.g., "AND" or "OR" at the beginning or end)
            filter = Regex.Replace(filter, @"^\s*(AND|OR)\s*|\s*(AND|OR)\s*$", "", RegexOptions.None, TimeSpan.FromSeconds(1));

            // 5. Remove leading or trailing operators inside parentheses (e.g., "( AND ...)" becomes "(...)")
            filter = Regex.Replace(filter, @"\(\s*(AND|OR)\s*", "(", RegexOptions.None, TimeSpan.FromSeconds(1));  // Remove leading operator inside parentheses
            filter = Regex.Replace(filter, @"\s*(AND|OR)\s*\)", ")", RegexOptions.None, TimeSpan.FromSeconds(1));  // Remove trailing operator inside parentheses

            // 6. Remove redundant outer parentheses with a single regex
            filter = Regex.Replace(filter, @"^\(([^()]+)\)$", "$1", RegexOptions.None, TimeSpan.FromSeconds(1));

            // 7. Remove multiple spaces or fix any accidental double spaces
            filter = Regex.Replace(filter, @"\s{2,}", " ", RegexOptions.None, TimeSpan.FromSeconds(1)).Trim();

            return filter;
        }

        // Method to ensure parentheses are balanced and remove redundant parentheses
        private static string RemoveRedundantParentheses(string input)
        {
            Stack<int> openParenIndices = new Stack<int>();
            List<int> indicesToRemove = new List<int>();
            char[] chars = input.ToCharArray();

            // Walk through the string to find parentheses
            for (int i = 0; i < chars.Length; i++)
            {
                if (chars[i] == '(')
                {
                    openParenIndices.Push(i); // Track index of opening parentheses
                }
                else if (chars[i] == ')')
                {
                    if (openParenIndices.Count > 0)
                    {
                        openParenIndices.Pop(); // Match the parentheses
                    }
                    else
                    {
                        indicesToRemove.Add(i); // Unmatched closing parenthesis
                    }
                }
            }

            // Remove extra open parentheses that don't have closing matches
            while (openParenIndices.Count > 0)
            {
                indicesToRemove.Add(openParenIndices.Pop());
            }

            // Build a new string without the redundant parentheses
            var filteredString = new StringBuilder();
            for (int i = 0; i < chars.Length; i++)
            {
                if (!indicesToRemove.Contains(i))
                {
                    filteredString.Append(chars[i]);
                }
            }

            return filteredString.ToString();
        }
    }
}
