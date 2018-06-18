using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using Cosmos.Extensions;
using Cosmos.Extensions.Exceptions;
using Cosmos.Search;
using Cosmos.Search.Filters;
using Humanizer;
using Newtonsoft.Json.Linq;

namespace PathValueDictionaryConvertor.Extensions
{
    public static class SearchFilterExtensions
    {
        private class PathValue
        {
            public string Path { get; set; }
            public object Value { get; set; }

            public PathValue(string path, object value)
            {
                Path = path;
                Value = value;
            }

            public override string ToString()
            {
                return $"{{{Path}, {Value}}}";
            }
        }

        // Some types we just want to take as is without diving deeper to get their properties.
        private static readonly Type[] ReflectionExclusions =
        {
            typeof(string),
            typeof(DateTimeOffset),
            typeof(DateTime)
        };
        
        internal static void NormaliseStringValueAtPath(this SearchFilter search, string path)
        {
            if (search == null)
            {
                return;
            }

            foreach (SearchFilter filter in search.Filters)
            {
                filter.NormaliseStringValueAtPath(path);
            }

            if (string.Equals(search.Path, path) && search.Value != null && search.Value is string stringValue)
            {
                search.Value = stringValue.ToLowerInvariant();
            }
        }

        public static SearchFilter ToConditionBasedSearchFilterOriginal(this object request, string pathToConditions = null)
        {
            List<PathValue> pathValues = GetPropertyPathValuesOriginal(request, pathToConditions ?? string.Empty, ".");

            if (!string.IsNullOrEmpty(pathToConditions))
            {
                pathToConditions += ".";
            }

            SearchFilter search = null;

            List<SpanDefinition> spans = GetSpans(request);
            if (spans != null)
            {
                foreach (SpanDefinition span in spans)
                {
                    switch (span.Type)
                    {
                        case SpanType.Intersection:
                            string fromPath = $"{pathToConditions}{span.From}";
                            PathValue from = pathValues.FirstOrDefault(p => p.Path == fromPath);
                            string toPath = $"{pathToConditions}{span.To}";
                            PathValue to = pathValues.FirstOrDefault(p => p.Path == toPath);

                            if (from == null || to == null)
                            {
                                continue;
                            }

                            pathValues.Remove(from);
                            pathValues.Remove(to);

                            search &= BuildIntersectionFilter(from, to);
                            break;
                        case SpanType.Between:
                            string path = $"{pathToConditions}{span.Property}";
                            object value = pathValues.FirstOrDefault(p => p.Path == path)?.Value;

                            if (value == null)
                            {
                                continue;
                            }

                            search &= BuildBetweenFilter($"{path}/{span.From}", $"{path}/{span.To}", value);
                            break;
                        default:
                            throw ExceptionFactory.CreateException("Span type must be specified.", HttpStatusCode.BadRequest);
                    }
                }
            }

            search = pathValues.Aggregate(search, (current, pv) => current & BuildSimpleFilter(pv));

            return search;
        }
        
        public static SearchFilter ToConditionBasedSearchFilterNew(this object request, string pathToConditions = null)
        {
            List<PathValue> pathValues = GetPropertyPathValuesNew(request, pathToConditions ?? string.Empty, ".");

            if (!string.IsNullOrEmpty(pathToConditions))
            {
                pathToConditions += ".";
            }

            SearchFilter search = null;

            List<SpanDefinition> spans = GetSpans(request);
            if (spans != null)
            {
                foreach (SpanDefinition span in spans)
                {
                    switch (span.Type)
                    {
                        case SpanType.Intersection:
                            string fromPath = $"{pathToConditions}{span.From}";
                            PathValue from = pathValues.FirstOrDefault(p => p.Path == fromPath);
                            string toPath = $"{pathToConditions}{span.To}";
                            PathValue to = pathValues.FirstOrDefault(p => p.Path == toPath);

                            if (from == null || to == null)
                            {
                                continue;
                            }

                            pathValues.Remove(from);
                            pathValues.Remove(to);

                            search &= BuildIntersectionFilter(from, to);
                            break;
                        case SpanType.Between:
                            string path = $"{pathToConditions}{span.Property}";
                            object value = pathValues.FirstOrDefault(p => p.Path == path)?.Value;

                            if (value == null)
                            {
                                continue;
                            }

                            search &= BuildBetweenFilter($"{path}/{span.From}", $"{path}/{span.To}", value);
                            break;
                        default:
                            throw ExceptionFactory.CreateException("Span type must be specified.", HttpStatusCode.BadRequest);
                    }
                }
            }

            search = pathValues.Aggregate(search, (current, pv) => current & BuildSimpleFilter(pv));

            return search;
        }
        
        
        public static SearchFilter AndUnique(this SearchFilter a, SearchFilter b)
        {
            if (a == null) return b;
            if (b == null) return a;

            var subfilters = new List<SearchFilter>();
            var combined = new SearchFilter
            {
                Operation = SearchFilterOperation.And,
                Filters = subfilters
            };
            
            AppendUniqueFilter(subfilters, a, SearchFilterOperation.And);
            AppendUniqueFilter(subfilters, b, SearchFilterOperation.And);

            return combined;
        }

        private static void AppendUniqueFilter(List<SearchFilter> combinedfilters, SearchFilter filter, SearchFilterOperation operation)
        {
            RemoveDuplicated(combinedfilters, filter, operation);
            AppendFilter(combinedfilters, filter, operation);
        }

        private static void RemoveDuplicated(List<SearchFilter> combinedfilters, SearchFilter filter, SearchFilterOperation operation)
        {
            List<SearchFilter> duplication;
            var comparer = new SearchFilterPathComparer();
            
            if (filter.Operation == operation)
            {
                duplication = combinedfilters.Intersect(filter.Filters, comparer).ToList();
            }
            else
            {
                duplication = combinedfilters.Where(f => comparer.Equals(f, filter)).ToList();
            }

            combinedfilters.RemoveRange(duplication);
        }
        
        public static void AppendFilter(List<SearchFilter> subfilters, SearchFilter filter, SearchFilterOperation operation)
        {
            if (filter.Operation == operation)
            {
                subfilters.AddRange(filter.Filters);
            }
            else
            {
                subfilters.Add(filter);
            }
        }

       private static List<PathValue> GetPropertyPathValuesOriginal(this object target, string basePath, string separator)
        {
            var localProperties = new List<PathValue>();
            if (target == null || ReflectionExclusions.Contains(target.GetType()))
            {
                return localProperties;
            }

            if (!string.IsNullOrEmpty(basePath))
            {
                basePath += separator;
            }

            if (target is IDictionary dictionary)
            {
                foreach (string key in dictionary.Keys)
                {
                    if (key == "Spans")
                    {
                        continue;
                    }

                    var value = dictionary[key];

                    string localPath = $"{basePath}{key.Camelize()}";
                    List<PathValue> properties = GetPropertyPathValuesOriginal(value, localPath, "-");
                    if (properties.Count == 0 && !(value is IDictionary))
                    {
                        localProperties.Add(new PathValue(localPath, value));
                    }
                    else
                    {
                        localProperties.AddRange(properties);
                    }
                }
            }
            else if (target is IEnumerable array)
            {
                foreach (object item in array)
                {
//                    string localPath = $"{basePath}{key.Camelize()}";
                     
                    List<PathValue> properties = GetPropertyPathValuesOriginal(item, basePath, "-");
                    if (properties.Count == 0 && !(item is IDictionary))
                    {
                        localProperties.Add(new PathValue(basePath, item));
                    }
                    else
                    {
                        localProperties.AddRange(properties);
                    }
                }
            }
            else
            {
                PropertyInfo[] properties = target.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public);
                foreach (PropertyInfo property in properties)
                {
                    if (property.Name == "Spans" ||
                        property.GetIndexParameters().Length != 0)
                    {
                        continue;
                    }

                    string localPath = $"{basePath}{property.Name.Camelize()}";
                    object value = property.GetValue(target);
                    List<PathValue> nestedProperties = GetPropertyPathValuesOriginal(value, localPath, "-");
                    if (nestedProperties.Count == 0 && !(value is IDictionary))
                    {
                        localProperties.Add(new PathValue(localPath, value));
                    }
                    else
                    {
                        localProperties.AddRange(nestedProperties);
                    }
                }
            }

            return localProperties;
        }           
        
        private static List<PathValue> GetPropertyPathValuesNew(object target, string basePath, string separator)
        {
            var localProperties = new List<PathValue>();
            if (target == null || ReflectionExclusions.Contains(target.GetType()))
            {
                return localProperties;
            }

            if (!string.IsNullOrEmpty(basePath))
            {
                basePath += separator;
            }

            var jObj = JObject.FromObject(target);
            
            var descendants = jObj.Descendants().ToList();

            foreach (var child in descendants)
            {
                if (child.HasValues || child.Path == "Spans" )//|| child is JContainer)
                {
                    continue;
                }

                if (child is JValue jValue)
                {
                    var childPath = child.Path.Trim('[').Trim(']').Replace("'", string.Empty);
                    var key = childPath.Camelize(); 
                    key = string.Join("-", key.Split('.').Select(x => x.Camelize()));
                    string localPath = $"{basePath}{key}";
                    localProperties.Add(new PathValue(localPath, jValue.Value));
                }
            }
            
            return localProperties;
        }

        private static List<SpanDefinition> GetSpans(object request)
        {
            object spans = request.GetValue("Spans");

            if (spans == null)
            {
                return null;
            }

            if (spans is IEnumerable<SpanDefinition> spansCollection)
            {
                return spansCollection.ToList();
            }

            if (spans is IEnumerable<object> dynamicCollection)
            {
                // TODO: try to map each object to SpanDefinition.
            }

            return null;
        }

        private static SearchFilter BuildBetweenFilter(string fromPath, string toPath, object value)
        {
            string typeString = GetTypeString(value);
            string fromOperationPath = $"{fromPath}.operator";
            string fromTypedPath = $"{fromPath}.value.{typeString}";
            string toOperationPath = $"{toPath}.operator";
            string toTypedPath = $"{toPath}.value.{typeString}";

            var fromfilter = new SearchFilter(fromPath, SearchFilterOperation.EqualTo, null) |
                   (new SearchFilter(fromOperationPath, "GreaterThanEqual") &
                    new SearchFilter(fromTypedPath, SearchFilterOperation.LessThanOrEqualTo, value)) |
                   (new SearchFilter(fromOperationPath, "GreaterThan") &
                    new SearchFilter(fromTypedPath, SearchFilterOperation.LessThan, value)) |
                   (new SearchFilter(fromOperationPath, "Equal") &
                    new SearchFilter(fromTypedPath, SearchFilterOperation.EqualTo, value));

            var tofilter = new SearchFilter(fromPath, SearchFilterOperation.EqualTo, null) |
                   (new SearchFilter(toOperationPath, "Equal") &
                    new SearchFilter(toTypedPath, SearchFilterOperation.EqualTo, value)) |
                   (new SearchFilter(toOperationPath, "LessThan") &
                    new SearchFilter(toTypedPath, SearchFilterOperation.GreaterThan, value)) |
                   (new SearchFilter(toOperationPath, "LessThanEqual") &
                    new SearchFilter(toTypedPath, SearchFilterOperation.GreaterThanOrEqualTo, value));

            return fromfilter & tofilter;
        }

        private static SearchFilter BuildIntersectionFilter(PathValue from, PathValue to)
        {
            string fromTypeString = GetTypeString(from.Value);
            string fromOperationPath = $"{from.Path}.operator";
            string fromTypedPath = $"{from.Path}.value.{fromTypeString}";
            string toTypeString = GetTypeString(to.Value);
            string toOperationPath = $"{to.Path}.operator";
            string toTypedPath = $"{to.Path}.value.{toTypeString}";

            SearchFilter fromFilter, toFilter, spanFilter;

            if (to.Value == null)
            {
                if (from.Value == null)
                {
                    return null; // if span from/to are both null, get all items.
                }

                fromFilter = (new SearchFilter(fromOperationPath, "GreaterThanEqual") &
                              new SearchFilter(fromTypedPath, SearchFilterOperation.LessThanOrEqualTo, from.Value) &
                              new SearchFilter(toTypedPath, SearchFilterOperation.EqualTo, from.Value)) |
                             (new SearchFilter(fromOperationPath, "GreaterThan") &
                              new SearchFilter(fromTypedPath, SearchFilterOperation.LessThan, from.Value) &
                              new SearchFilter(toTypedPath, SearchFilterOperation.EqualTo, from.Value)) |
                             (new SearchFilter(fromOperationPath, "Equal") &
                              new SearchFilter(fromTypedPath, SearchFilterOperation.EqualTo, from.Value)) |
                             (new SearchFilter(fromOperationPath, "LessThan") &
                              new SearchFilter(fromTypedPath, SearchFilterOperation.GreaterThan, from.Value)) |
                             (new SearchFilter(fromOperationPath, "LessThanEqual") &
                              new SearchFilter(fromTypedPath, SearchFilterOperation.GreaterThanOrEqualTo, from.Value));

                toFilter = new SearchFilter(to.Path, SearchFilterOperation.EqualTo, null) &
                           new SearchFilter(fromTypedPath, SearchFilterOperation.LessThanOrEqualTo, from.Value);

                spanFilter = new SearchFilter(fromTypedPath, SearchFilterOperation.GreaterThanOrEqualTo, from.Value) &
                             new SearchFilter(to.Path, SearchFilterOperation.EqualTo, null);
            }
            else if (from.Value == null)
            {
                fromFilter = new SearchFilter(from.Path, SearchFilterOperation.EqualTo, null) &
                             new SearchFilter(toTypedPath, SearchFilterOperation.GreaterThanOrEqualTo, to.Value);

                toFilter = (new SearchFilter(toOperationPath, "GreaterThanEqual") &
                            new SearchFilter(toTypedPath, SearchFilterOperation.LessThanOrEqualTo, to.Value)) |
                           (new SearchFilter(toOperationPath, "GreaterThan") &
                            new SearchFilter(toTypedPath, SearchFilterOperation.LessThan, to.Value)) |
                           (new SearchFilter(toOperationPath, "Equal") &
                            new SearchFilter(toTypedPath, SearchFilterOperation.EqualTo, to.Value)) |
                           (new SearchFilter(toOperationPath, "LessThan") &
                            new SearchFilter(toTypedPath, SearchFilterOperation.GreaterThan, to.Value) &
                            new SearchFilter(fromTypedPath, SearchFilterOperation.EqualTo, to.Value)) |
                           (new SearchFilter(toOperationPath, "LessThanEqual") &
                            new SearchFilter(toTypedPath, SearchFilterOperation.GreaterThanOrEqualTo, to.Value) &
                            new SearchFilter(fromTypedPath, SearchFilterOperation.EqualTo, to.Value));

                spanFilter = new SearchFilter(from.Path, SearchFilterOperation.EqualTo, null) &
                             new SearchFilter(toTypedPath, SearchFilterOperation.LessThanOrEqualTo, to.Value);
            }
            else
            {
                fromFilter = (new SearchFilter(fromOperationPath, "GreaterThanEqual") &
                              new SearchFilter(fromTypedPath, SearchFilterOperation.LessThanOrEqualTo, from.Value) &
                              new SearchFilter(toTypedPath, SearchFilterOperation.GreaterThanOrEqualTo, from.Value)) |
                             (new SearchFilter(fromOperationPath, "GreaterThan") &
                              new SearchFilter(fromTypedPath, SearchFilterOperation.LessThan, from.Value) &
                              new SearchFilter(toTypedPath, SearchFilterOperation.GreaterThanOrEqualTo, from.Value)) |
                             (new SearchFilter(fromOperationPath, "Equal") &
                              new SearchFilter(fromTypedPath, SearchFilterOperation.EqualTo, from.Value)) |
                             (new SearchFilter(fromOperationPath, "LessThan") &
                              new SearchFilter(fromTypedPath, SearchFilterOperation.GreaterThan, from.Value)) |
                             (new SearchFilter(fromOperationPath, "LessThanEqual") &
                              new SearchFilter(fromTypedPath, SearchFilterOperation.GreaterThanOrEqualTo, from.Value));

                toFilter = (new SearchFilter(toOperationPath, "GreaterThanEqual") &
                            new SearchFilter(toTypedPath, SearchFilterOperation.LessThanOrEqualTo, to.Value)) |
                           (new SearchFilter(toOperationPath, "GreaterThan") &
                            new SearchFilter(toTypedPath, SearchFilterOperation.LessThan, to.Value)) |
                           (new SearchFilter(toOperationPath, "Equal") &
                            new SearchFilter(toTypedPath, SearchFilterOperation.EqualTo, to.Value)) |
                           (new SearchFilter(toOperationPath, "LessThan") &
                            new SearchFilter(toTypedPath, SearchFilterOperation.GreaterThan, to.Value) &
                            new SearchFilter(fromTypedPath, SearchFilterOperation.LessThanOrEqualTo, to.Value)) |
                           (new SearchFilter(toOperationPath, "LessThanEqual") &
                            new SearchFilter(toTypedPath, SearchFilterOperation.GreaterThanOrEqualTo, to.Value) &
                            new SearchFilter(fromTypedPath, SearchFilterOperation.LessThanOrEqualTo, to.Value));

                spanFilter = new SearchFilter(fromTypedPath, SearchFilterOperation.GreaterThanOrEqualTo, from.Value) &
                             new SearchFilter(toTypedPath, SearchFilterOperation.LessThanOrEqualTo, to.Value);
            }

            return fromFilter | toFilter | spanFilter;
        }

        private static SearchFilter BuildSimpleFilter(PathValue pathValue)
        {
            string typeString = GetTypeString(pathValue.Value);
            string operationPath = $"{pathValue.Path}.operator";
            string typedPath = $"{pathValue.Path}.value.{typeString}";
            string operationArrayPath = $"{pathValue.Path}.values.operator";
            string typedArrayPath = $"{pathValue.Path}.values.value.{typeString}";

            var nullSearch = new SearchFilter(pathValue.Path, SearchFilterOperation.EqualTo, null);

            if (pathValue.Value == null)
            {
                return nullSearch;
            }

            if (typeString == "bool" || typeString == "object")
            {
                SearchFilter BuildFilter(string opPath, string typePath)
                {
                    return new SearchFilter(opPath, "Equal") &
                           new SearchFilter(typePath, SearchFilterOperation.EqualTo, pathValue.Value);

                }

                return nullSearch |
                       BuildFilter(operationPath, typedPath) | 
                       BuildFilter(operationArrayPath, typedArrayPath);
            }
            else
            {
                SearchFilter BuildFilter(string opPath, string typePath)
                {
                    return (new SearchFilter(opPath, "GreaterThanEqual") &
                            new SearchFilter(typePath, SearchFilterOperation.LessThanOrEqualTo, pathValue.Value)) |
                           (new SearchFilter(opPath, "GreaterThan") &
                            new SearchFilter(typePath, SearchFilterOperation.LessThan, pathValue.Value)) |
                           (new SearchFilter(opPath, "Equal") &
                            new SearchFilter(typePath, SearchFilterOperation.EqualTo, pathValue.Value)) |
                           (new SearchFilter(opPath, "LessThan") &
                            new SearchFilter(typePath, SearchFilterOperation.GreaterThan, pathValue.Value)) |
                           (new SearchFilter(opPath, "LessThanEqual") &
                            new SearchFilter(typePath, SearchFilterOperation.GreaterThanOrEqualTo, pathValue.Value));

                }

                return nullSearch |
                       BuildFilter(operationPath, typedPath) |
                       BuildFilter(operationArrayPath, typedArrayPath);
            }
        }

        private static string GetTypeString(object value)
        {
            switch (value)
            {
                case string _:
                    return "string";
                case bool _:
                    return "bool";
                case int _:
                    return "int";
                case long _:
                    return "int";
                case double _:
                    return "double";
                case DateTimeOffset _:
                    return "dateTimeOffset";
                case DateTime _:
                    return "dateTime";
                case IEnumerable e:
                    return "values." + GetTypeString(e.Cast<object>().FirstOrDefault());
                default:
                    return "object";
            }
        }
    }
}