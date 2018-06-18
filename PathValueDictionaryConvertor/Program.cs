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
using PathValueDictionaryConvertor.Extensions;

namespace PathValueDictionaryConvertor
{
    public class PathValue
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

    public class Target
    {
        public string Name { get; set; }
        public Guid Id { get; set; }

        public Target(string name)
        {
            Name = name;
            Id = Guid.NewGuid();
        }
    } 

    public class Program 
    {
        private static readonly Type[] ReflectionExclusions =
        {
            typeof(string),
            typeof(DateTimeOffset),
            typeof(DateTime)
        };
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
//        public static SearchFilter ToConditionBasedSearchFilter(object request, string pathToConditions = null)
//        {
//            List<PathValue> pathValues = GetPropertyPathValuesOriginal(request, pathToConditions ?? string.Empty, ".");
//
//            if (!string.IsNullOrEmpty(pathToConditions))
//            {
//                pathToConditions += ".";
//            }
//
//            SearchFilter search = null;
//
//            List<SpanDefinition> spans = GetSpans(request);
//            if (spans != null)
//            {
//                foreach (SpanDefinition span in spans)
//                {
//                    switch (span.Type)
//                    {
//                        case SpanType.Intersection:
//                            string fromPath = $"{pathToConditions}{span.From}";
//                            PathValue from = pathValues.FirstOrDefault(p => p.Path == fromPath);
//                            string toPath = $"{pathToConditions}{span.To}";
//                            PathValue to = pathValues.FirstOrDefault(p => p.Path == toPath);
//
//                            if (from == null || to == null)
//                            {
//                                continue;
//                            }
//
//                            pathValues.Remove(from);
//                            pathValues.Remove(to);
//
//                            search &= BuildIntersectionFilter(from, to);
//                            break;
//                        case SpanType.Between:
//                            string path = $"{pathToConditions}{span.Property}";
//                            object value = pathValues.FirstOrDefault(p => p.Path == path)?.Value;
//
//                            if (value == null)
//                            {
//                                continue;
//                            }
//
//                            search &= BuildBetweenFilter($"{path}/{span.From}", $"{path}/{span.To}", value);
//                            break;
//                        default:
//                            throw ExceptionFactory.CreateException("Span type must be specified.", HttpStatusCode.BadRequest);
//                    }
//                }
//            }
//
//            search = pathValues.Aggregate(search, (current, pv) => current & BuildSimpleFilter(pv));
//
//            return search;
//        }
        private static List<PathValue> GetPropertyPathValuesOriginal(object target, string basePath, string separator)
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
                if (child.HasValues || child.Path == "Spans" || child is JContainer)
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
        
        static void Main(string[] args)
        {

            
//            var test = new
//            {
//                ParentField1 = new
//                {
//                    ChildField1 = "Child1_1",
//                    ChildField2 = new
//                    {
//                        ChildField2_1 = " Child2_1"
//                    },
//                    //ChildField3 = new Target[]{new Target("t1"), new Target("t2"), new Target("t3")  }
//                }
//            };
//            
                        
            var p3 = new
            {
                Id = new
                {
                    ProductId = Guid.NewGuid()
                }
            };
            
//            var testObject = new Dictionary<string, object>()
//            {
//                {"key1",  "val1"},
//                {"key2",  new {Name = "val2", Value = 34}},
//                {"key3",  "val3"},
//                {"key4",  test},
//                {"key5",  "val5"},
//                
//            };            
//            var testObject = new Dictionary<string, dynamic>()
//            {
//                {"product",  p3}
//            };


//            var testObject = new[]
//            {
//                "str1",
//                "str2",
//                "str3",
//            };

//            var keyValuePairs_Original = GetPropertyPathValuesOriginal(testObject, "condition", ".");
//            var keyValuePairs_New = GetPropertyPathValuesNew(testObject, "condition", ".");
//
//
//            Console.WriteLine("Original o/p");
//            foreach (var pair in keyValuePairs_Original)
//            {
//                Console.WriteLine($"{pair.Path} => {pair.Value}");               
//            }
//            var testObject = new
//            {
//                ParentField1 = new
//                {
//                    ChildField1 = "Child1_1",
//                    ChildField2 = new
//                    {
//                        ChildField2_1 = " Child2_1"
//                    },
//                    ChildField3 = new Target[]{new Target("t1"), new Target("t2"), new Target("t3")  }
//                }
//            };
            
            
            
            var testObject = new
            {
                ParentField1 = new
                {

                    ChildField3 = new Target[]{new Target("t1"), new Target("t2"), new Target("t3")  }
                }
            };

            var conditionBasedSearchFilterOriginal = testObject.ToConditionBasedSearchFilterOriginal("appliesTo.conditionData");
            var searchOptionsOriginal = new SearchOptions(){Search = conditionBasedSearchFilterOriginal};
            Console.WriteLine(searchOptionsOriginal);
            
            Console.WriteLine("\n=============================================================== \n");
            
//            Console.WriteLine("New o/p");
//            foreach (var pair in keyValuePairs_New)
//            {
//                Console.WriteLine($"{pair.Path} => {pair.Value}");               
//            }
            
            var conditionBasedSearchFilterNew = testObject.ToConditionBasedSearchFilterNew("appliesTo.conditionData");
            var searchOptionsNew = new SearchOptions(){Search = conditionBasedSearchFilterNew};
            Console.WriteLine(searchOptionsNew);

        }
    }
}