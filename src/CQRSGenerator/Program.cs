using Microsoft.EntityFrameworkCore;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Design;
using PluralizeService.Core;
using System.Text;
using SampleApp.Domain.Common;
using System.Collections.Generic;
using SampleApp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using IdentityServer4.EntityFramework.Options;

namespace CQRSGenerator
{
    class Program
    {
        static readonly string entities_namespace = "SampleApp.Domain.Entities";
        static readonly string enities_assembly = "SampleApp.Domain";

        static readonly string dbContext_interface_namespace = "SampleApp.Application.Common.Interfaces";
        static readonly string dbContext_interface = "IApplicationDbContext";

        static readonly string codeGenerateion_namespace = "SampleApp.Application.CQRS";
        static readonly string codeGenerateion_path = "SampleApp.Application\\CQRS";
        
        static readonly Dictionary<Type, string> typesMapping = new Dictionary<Type, string>();

        // It's very important to set the dbcontext here which is this case is ApplicationDbContext
        static ApplicationDbContext dbContext = null;

        // You can define a list of general property names to be excleded when generating create command.
        static readonly List<string> excluded_properties_create = new List<string>() { "Id", "CreatedBy", "Created", "LastModifiedBy", "LastModified" };

        // You can define a list of property names specific to each entity,to be excleded when generating create command.
        static readonly Dictionary<string, List<string>> excluded_properties_create_mapping = new Dictionary<string, List<string>>();

        static void Main(string[] args)
        {
            DbContextOptionsBuilder optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase("SQRS");
            OperationalStoreOptions storeOptions = new OperationalStoreOptions();
            IOptions<OperationalStoreOptions> optionParameter = Options.Create(storeOptions);
            dbContext = new ApplicationDbContext(optionsBuilder.Options, optionParameter);

            PreLoadTypeMappings();
            PreLoadCreateExcludedProperties();

            Assembly assembly = Assembly.Load(enities_assembly);
            Type[] entityList = assembly
                .GetTypes()
                .Where(t => t.IsClass && t.Namespace == entities_namespace)
                .ToArray();

            foreach (Type entity in entityList.Where(t => t.BaseType == typeof(AuditableEntity)))
            {
                GenerateCreateCommand(entity);
            }
        }

        /// <summary>
        /// Adds the mappings for the type definitions
        /// </summary>
        static void PreLoadTypeMappings()
        {
            typesMapping.Add(typeof(sbyte), "sbyte");
            typesMapping.Add(typeof(short), "short");
            typesMapping.Add(typeof(int), "int");
            typesMapping.Add(typeof(long), "long");
            typesMapping.Add(typeof(byte), "byte");
            typesMapping.Add(typeof(ushort), "ushort");
            typesMapping.Add(typeof(uint), "uint");
            typesMapping.Add(typeof(ulong), "ulong");
            typesMapping.Add(typeof(char), "char");
            typesMapping.Add(typeof(float), "float");
            typesMapping.Add(typeof(double), "double");
            typesMapping.Add(typeof(decimal), "decimal");
            typesMapping.Add(typeof(bool), "bool");
            typesMapping.Add(typeof(string), "string");
        }

        /// <summary>
        /// Excludes some properties for each entity
        /// </summary>
        private static void PreLoadCreateExcludedProperties()
        {
            var Excludes_TodoList = new List<string>() { "Items" };
            excluded_properties_create_mapping.Add("TodoList", Excludes_TodoList);
        }

        /// <summary>
        /// Generates C# code file containig create command for the specified entity
        /// </summary>
        /// <param name="entity">entity type</param>
        private static void GenerateCreateCommand(Type entity)
        {
            string fileName = GetGenerationFilePath("Commands", "Create", entity.Name);
            string template = File.ReadAllText("CreateCommandTemplate.txt");

            // adding default namespaces
            List<string> namespaces = new List<string>();
            namespaces.Add("using System;");
            namespaces.Add("using System.Collections.Generic;");
            namespaces.Add("using MediatR;");
            namespaces.Add("using System.Threading;");
            namespaces.Add("using System.Threading.Tasks;");
            namespaces.Add($"using {dbContext_interface_namespace};");
            namespaces.Add($"using {entities_namespace};");

            template = template.Replace("<#codeGenerateion_namespace#>", codeGenerateion_namespace);
            template = template.Replace("<#dbContext_interface#>", dbContext_interface);

            string className = $"Create{entity.Name}Command";
            template = template.Replace("<#ClassName#>", className);

            template = template.Replace("<#Entity#>", entity.Name);
            string entitySet = PluralizationProvider.Pluralize(entity.Name);
            template = template.Replace("<#EntitySet#>", entitySet);

            string keyType = FindKeyTypeForEntity(entity);
            template = template.Replace("<#returnType#>", keyType);

            // generating properties
            StringBuilder sb_definitions = new StringBuilder();
            StringBuilder sb_assigments = new StringBuilder();

            foreach (var p in entity.GetProperties().Where(x => x.CanWrite && x.CanRead && x.MemberType == MemberTypes.Property))
            {
                // exclude this property if it's in the general exclution list or in the specific list for current entity
                if (excluded_properties_create.Contains(p.Name) ||
                    (excluded_properties_create_mapping.ContainsKey(entity.Name) &&
                    excluded_properties_create_mapping.GetValueOrDefault(entity.Name).Contains(p.Name)))
                    continue;

                // declare the property for command request
                string p_type = GetTypeToDecalre(p.PropertyType, namespaces);
                sb_definitions.Append($"public {p_type} {p.Name} {{ set; get; }}");
                sb_definitions.Append(Environment.NewLine + "\t\t");

                // assign request properties to the entity
                sb_assigments.Append($"{p.Name} = request.{p.Name},");
                sb_assigments.Append(Environment.NewLine + "\t\t\t\t\t");
            }

            // writing namespaces
            template = template.Replace("<#namespaces#>", string.Join(Environment.NewLine, namespaces));

            // writing properties
            template = template.Replace("<#Properties#>", sb_definitions.ToString());

            // writing assigments inside command handler
            template = template.Replace("<#PropertiesAssigments#>", sb_assigments.ToString());

            // writing generated code inside the file
            File.WriteAllText(fileName, template);
        }

        /// <summary>
        /// Finds the key property of the entity which we use it as the response type for create command.
        /// </summary>
        /// <param name="entity">entity type to check</param>
        /// <returns>string type of the key property</returns>
        private static string FindKeyTypeForEntity(Type entity)
        {
            var dd = dbContext.Model.FindEntityType(entity)
                 .FindPrimaryKey()
                 .Properties
                 .Select(p => p)
                 .FirstOrDefault();

            return GetTypeToDecalre(dd.PropertyInfo.PropertyType, new List<string>());
        }

        /// <summary>
        /// Generates a string path to place the code file.
        /// </summary>
        /// <param name="mode">Command or Query</param>
        /// <param name="operation">Create, Delete, Update, Select</param>
        /// <param name="entity">entity type</param>
        /// <param name="isValidator">wether it is for generating the command validator or not</param>
        /// <returns></returns>
        private static string GetGenerationFilePath(string mode, string operation, string entity, bool isValidator = false)
        {
            string entitySet = PluralizationProvider.Pluralize(entity);
            string solutionDirectory = Directory.GetCurrentDirectory().Substring(0, Directory.GetCurrentDirectory().IndexOf("CQRSGenerator"));
            string dir = Path.Combine(solutionDirectory, codeGenerateion_path, entitySet, mode, operation + entity);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            string modDir = mode == "Commands" ? "Commmand" : "Query";
            string fileName = $"{operation}{entity}{modDir}";
            fileName += isValidator ? "Validator.cs" : ".cs";
            return Path.Combine(dir, fileName);
        }

        /// <summary>
        /// Use this to get the string type of the property of an entity
        /// </summary>
        /// <param name="type">entity type</param>
        /// <param name="namespaces">list of namespaces to add necessary namespaces based on the type</param>
        /// <returns></returns>
        private static string GetTypeToDecalre(Type type, List<string> namespaces)
        {
            string typeToGenerate = typesMapping.ContainsKey(type) ?
                typesMapping.GetValueOrDefault(type) :
                type.Name;

            if (type.IsGenericType)
            {
                typeToGenerate = typeToGenerate.Replace("`1", "");
                if (type.GenericTypeArguments.Length > 0)
                {
                    typeToGenerate += $"<{GetTypeToDecalre(type.GenericTypeArguments[0], namespaces)}>";
                }
            }
            
            if (Nullable.GetUnderlyingType(type) != null && type.GenericTypeArguments.Length > 0)
            {
                typeToGenerate = $"Nullable<{GetTypeToDecalre(type.GenericTypeArguments[0], namespaces)}>";
            }

            // check if it's required to add a refrence
            string namespaceToAdd = $"using {type.Namespace};";
            if (!namespaces.Contains(namespaceToAdd))
            {
                namespaces.Add(namespaceToAdd);
            }

            return typeToGenerate;
        }
       
    }
}
