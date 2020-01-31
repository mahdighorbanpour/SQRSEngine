using Microsoft.EntityFrameworkCore;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using PluralizeService.Core;
using System.Text;
using System.Collections.Generic;
using Microsoft.Extensions.Options;
using IdentityServer4.EntityFramework.Options;
using SampleApp.Domain.Common;
using SampleApp.Infrastructure.Persistence;

namespace CQRSGenerator
{
    class Program
    {
        static readonly string entities_namespace = "SampleApp.Domain.Entities";
        static readonly string enities_assembly = "SampleApp.Domain";

        static readonly string exceptions_namespace = "SampleApp.Application.Common.Exceptions";
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

        // You can define a list of general property names to be excleded when generating create command.
        static readonly List<string> excluded_properties_update = new List<string>() { "CreatedBy", "Created", "LastModifiedBy", "LastModified" };

        // You can define a list of property names specific to each entity,to be excleded when generating create command.
        static readonly Dictionary<string, List<string>> excluded_properties_update_mapping = new Dictionary<string, List<string>>();


        static void Main(string[] args)
        {
            DbContextOptionsBuilder optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase("SQRS");
            OperationalStoreOptions storeOptions = new OperationalStoreOptions();
            IOptions<OperationalStoreOptions> optionParameter = Options.Create(storeOptions);
            dbContext = new ApplicationDbContext(optionsBuilder.Options, optionParameter);

            PreLoadTypeMappings();
            PreLoadCreateExcludedProperties();
            PreLoadUpdateExcludedProperties();

            // loading enetites assembly
            Assembly assembly = Assembly.Load(enities_assembly);
            Type[] entityList = assembly
                .GetTypes()
                .Where(t => t.IsClass && t.Namespace == entities_namespace && !t.IsInterface && !t.IsAbstract)
                .ToArray();

            foreach (Type entity in entityList.Where(t => t.BaseType == typeof(AuditableEntity)))
            {
                GenerateCreateCommand(entity);
                GenerateCreateCommandValidator(entity);
                GenerateUpdateCommand(entity);
                GenerateUpdateCommandValidator(entity);
                GenerateDeleteCommand(entity);
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
        /// Excludes some properties for each entity
        /// </summary>
        private static void PreLoadUpdateExcludedProperties()
        {
            var Excludes_TodoList = new List<string>() { "List" };
            excluded_properties_update_mapping.Add("TodoItem", Excludes_TodoList);
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

            var key = FindKeyPropertyForEntity(entity);
            template = template.Replace("<#returnType#>", GetTypeToDecalre(key.PropertyType, namespaces));

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
        /// Generates C# code file containig create validator command for the specified entity
        /// </summary>
        /// <param name="entity">entity type</param>
        private static void GenerateCreateCommandValidator(Type entity)
        {
            string fileName = GetGenerationFilePath("Commands", "Create", entity.Name, true);
            string template = File.ReadAllText("CreateCommandValidatorTemplate.txt");

            template = template.Replace("<#codeGenerateion_namespace#>", codeGenerateion_namespace);

            string className = $"Create{entity.Name}Command";
            template = template.Replace("<#ClassName#>", className);

            template = template.Replace("<#Entity#>", entity.Name);
            string entitySet = PluralizationProvider.Pluralize(entity.Name);
            template = template.Replace("<#EntitySet#>", entitySet);

            // generating properties
            StringBuilder sb_rules = new StringBuilder();

            var annotations = dbContext.Model.FindEntityType(entity).GetAnnotations();
            var CheckConstraints = dbContext.Model.FindEntityType(entity).GetCheckConstraints();
            var DeclaredForeignKeys = dbContext.Model.FindEntityType(entity).GetDeclaredForeignKeys();
            var DeclaredNavigations = dbContext.Model.FindEntityType(entity).GetDeclaredNavigations();
            var DeclaredProperties = dbContext.Model.FindEntityType(entity).GetDeclaredProperties();
            var DeclaredReferencingForeignKeys = dbContext.Model.FindEntityType(entity).GetDeclaredReferencingForeignKeys();

            foreach (var p in dbContext.Model.FindEntityType(entity).GetDeclaredProperties().Where(x => x.ValueGenerated == Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never))
            {
                // exclude this property if it's in the general exclution list or in the specific list for current entity
                if (excluded_properties_create.Contains(p.Name) ||
                    (excluded_properties_create_mapping.ContainsKey(entity.Name) &&
                    excluded_properties_create_mapping.GetValueOrDefault(entity.Name).Contains(p.Name)))
                    continue;

                // declare rules for the property
                List<string> rules = new List<string>();

                // check if it's required. exclude types that are not null by default and have a default value like int
                if (!p.IsNullable && !p.ClrType.IsValueType)
                {
                    if (p.ClrType == typeof(string))
                        rules.Add(".NotEmpty()");
                    else
                        rules.Add(".NotNull()");
                }

                // check if it has a fixed length
                if (p.IsFixedLength())
                    rules.Add($".Length({p.GetMaxLength()})");
                else if (p.GetMaxLength().HasValue) // check if it has a max length
                    rules.Add($".MaximumLength({p.GetMaxLength()})");

                if (rules.Count > 0)
                {
                    sb_rules.Append($"RuleFor(v => v.{p.Name})" + Environment.NewLine + "\t\t\t\t");
                    sb_rules.Append(string.Join(Environment.NewLine + "\t\t\t\t", rules));
                    sb_rules.Append(";");
                    sb_rules.Append(Environment.NewLine + "\t\t\t");
                }
            }

            template = template.Replace("<#rules#>", sb_rules.ToString());

            // writing generated code inside the file
            File.WriteAllText(fileName, template);
        }

        /// <summary>
        /// Generates C# code file containig update command for the specified entity
        /// </summary>
        /// <param name="entity">entity type</param>
        private static void GenerateUpdateCommand(Type entity)
        {
            string fileName = GetGenerationFilePath("Commands", "Update", entity.Name);
            string template = File.ReadAllText("UpdateCommandTemplate.txt");

            // adding default namespaces
            List<string> namespaces = new List<string>();
            namespaces.Add("using System;");
            namespaces.Add("using System.Collections.Generic;");
            namespaces.Add("using MediatR;");
            namespaces.Add("using System.Threading;");
            namespaces.Add("using System.Threading.Tasks;");
            namespaces.Add($"using {dbContext_interface_namespace};");
            namespaces.Add($"using {exceptions_namespace};");
            namespaces.Add($"using {entities_namespace};");

            template = template.Replace("<#codeGenerateion_namespace#>", codeGenerateion_namespace);
            template = template.Replace("<#dbContext_interface#>", dbContext_interface);

            string className = $"Update{entity.Name}Command";
            template = template.Replace("<#ClassName#>", className);

            template = template.Replace("<#Entity#>", entity.Name);
            string entitySet = PluralizationProvider.Pluralize(entity.Name);
            template = template.Replace("<#EntitySet#>", entitySet);

            // generating properties
            StringBuilder sb_definitions = new StringBuilder();
            StringBuilder sb_assigments = new StringBuilder();

            foreach (var p in entity.GetProperties().Where(x => x.CanWrite && x.CanRead && x.MemberType == MemberTypes.Property))
            {
                // exclude this property if it's in the general exclution list or in the specific list for current entity
                if (excluded_properties_update.Contains(p.Name) ||
                    (excluded_properties_update_mapping.ContainsKey(entity.Name) &&
                    excluded_properties_update_mapping.GetValueOrDefault(entity.Name).Contains(p.Name)))
                    continue;

                // declare the property for command request
                string p_type = GetTypeToDecalre(p.PropertyType, namespaces);
                sb_definitions.Append($"public {p_type} {p.Name} {{ set; get; }}");
                sb_definitions.Append(Environment.NewLine + "\t\t");

                // assign request properties to the entity
                sb_assigments.Append($"entity.{p.Name} = request.{p.Name};");
                sb_assigments.Append(Environment.NewLine + "\t\t\t\t");
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
        /// Generates C# code file containig create validator command for the specified entity
        /// </summary>
        /// <param name="entity">entity type</param>
        private static void GenerateUpdateCommandValidator(Type entity)
        {
            string fileName = GetGenerationFilePath("Commands", "Update", entity.Name, true);
            string template = File.ReadAllText("UpdateCommandValidatorTemplate.txt");

            template = template.Replace("<#codeGenerateion_namespace#>", codeGenerateion_namespace);

            string className = $"Update{entity.Name}Command";
            template = template.Replace("<#ClassName#>", className);

            template = template.Replace("<#Entity#>", entity.Name);
            string entitySet = PluralizationProvider.Pluralize(entity.Name);
            template = template.Replace("<#EntitySet#>", entitySet);

            // generating properties
            StringBuilder sb_rules = new StringBuilder();

            var annotations = dbContext.Model.FindEntityType(entity).GetAnnotations();
            var CheckConstraints = dbContext.Model.FindEntityType(entity).GetCheckConstraints();
            var DeclaredForeignKeys = dbContext.Model.FindEntityType(entity).GetDeclaredForeignKeys();
            var DeclaredNavigations = dbContext.Model.FindEntityType(entity).GetDeclaredNavigations();
            var DeclaredProperties = dbContext.Model.FindEntityType(entity).GetDeclaredProperties();
            var DeclaredReferencingForeignKeys = dbContext.Model.FindEntityType(entity).GetDeclaredReferencingForeignKeys();

            foreach (var p in dbContext.Model.FindEntityType(entity).GetDeclaredProperties().Where(x => x.ValueGenerated == Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never))
            {
                // exclude this property if it's in the general exclution list or in the specific list for current entity
                if (excluded_properties_update.Contains(p.Name) ||
                    (excluded_properties_update_mapping.ContainsKey(entity.Name) &&
                    excluded_properties_update_mapping.GetValueOrDefault(entity.Name).Contains(p.Name)))
                    continue;

                // declare rules for the property
                List<string> rules = new List<string>();

                // check if it's required. exclude types that are not null by default and have a default value like int
                if (!p.IsNullable && !p.ClrType.IsValueType)
                {
                    if (p.ClrType == typeof(string))
                        rules.Add(".NotEmpty()");
                    else
                        rules.Add(".NotNull()");
                }

                // check if it has a fixed length
                if (p.IsFixedLength())
                    rules.Add($".Length({p.GetMaxLength()})");
                else if (p.GetMaxLength().HasValue) // check if it has a max length
                    rules.Add($".MaximumLength({p.GetMaxLength()})");

                if (rules.Count > 0)
                {
                    sb_rules.Append($"RuleFor(v => v.{p.Name})" + Environment.NewLine + "\t\t\t\t");
                    sb_rules.Append(string.Join(Environment.NewLine + "\t\t\t\t", rules));
                    sb_rules.Append(";");
                    sb_rules.Append(Environment.NewLine + "\t\t\t");
                }
            }

            template = template.Replace("<#rules#>", sb_rules.ToString());

            // writing generated code inside the file
            File.WriteAllText(fileName, template);
        }

        /// <summary>
        /// Generates C# code file containig delete command for the specified entity
        /// </summary>
        /// <param name="entity">entity type</param>
        private static void GenerateDeleteCommand(Type entity)
        {
            string fileName = GetGenerationFilePath("Commands", "Delete", entity.Name);
            string template = File.ReadAllText("DeleteCommandTemplate.txt");

            // adding default namespaces
            List<string> namespaces = new List<string>();
            namespaces.Add("using System;");
            namespaces.Add("using MediatR;");
            namespaces.Add("using System.Threading;");
            namespaces.Add("using System.Threading.Tasks;");
            namespaces.Add($"using {dbContext_interface_namespace};");
            namespaces.Add($"using {exceptions_namespace};");
            namespaces.Add($"using {entities_namespace};");

            template = template.Replace("<#codeGenerateion_namespace#>", codeGenerateion_namespace);
            template = template.Replace("<#dbContext_interface#>", dbContext_interface);

            string className = $"Delete{entity.Name}Command";
            template = template.Replace("<#ClassName#>", className);

            template = template.Replace("<#Entity#>", entity.Name);
            string entitySet = PluralizationProvider.Pluralize(entity.Name);
            template = template.Replace("<#EntitySet#>", entitySet);

            var key = FindKeyPropertyForEntity(entity);

            // generating properties
            StringBuilder sb_definitions = new StringBuilder();

            // declare the property for command request
            string p_type = GetTypeToDecalre(key.PropertyType, namespaces);
            sb_definitions.Append($"public {p_type} {key.Name} {{ set; get; }}");
            sb_definitions.Append(Environment.NewLine + "\t\t");


            // writing namespaces
            template = template.Replace("<#namespaces#>", string.Join(Environment.NewLine, namespaces));

            // writing properties
            template = template.Replace("<#Properties#>", sb_definitions.ToString());

            // writing generated code inside the file
            File.WriteAllText(fileName, template);
        }


        /// <summary>
        /// Finds the key property of the entity which we use it as the response type for create command.
        /// </summary>
        /// <param name="entity">entity type to check</param>
        /// <returns>string type of the key property</returns>
        private static PropertyInfo FindKeyPropertyForEntity(Type entity)
        {
            var key = dbContext.Model.FindEntityType(entity)
                 .FindPrimaryKey()
                 .Properties
                 .FirstOrDefault();
            return key?.PropertyInfo;
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
