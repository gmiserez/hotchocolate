using System;
using System.Collections.Generic;
using System.Linq;
using HotChocolate.Types;

namespace HotChocolate.Configuration
{
    /// <summary>
    /// Registers types and their depending types.
    /// </summary>
    internal class TypeRegistrar
    {
        private readonly Queue<INamedType> _queue;
        private readonly HashSet<string> _registered = new HashSet<string>();
        private readonly List<SchemaError> _errors = new List<SchemaError>();

        public TypeRegistrar(IEnumerable<INamedType> types)
        {
            if (types == null)
            {
                throw new ArgumentNullException(nameof(types));
            }

            _queue = new Queue<INamedType>(types);
        }

        public IReadOnlyCollection<SchemaError> Errors => _errors;

        public void RegisterTypes(
            ISchemaContext schemaContext,
            string queryTypeName)
        {
            RegisterAllTypes(schemaContext);
            RegisterTypeDependencies(schemaContext, queryTypeName);
            RegisterDirectiveDependencies(schemaContext);
        }

        private void RegisterAllTypes(ISchemaContext context)
        {
            foreach (INamedType type in _queue)
            {
                context.Types.RegisterType(type);
            }
        }

        private void RegisterTypeDependencies(
            ISchemaContext context, string queryTypeName)
        {
            // register types until there are no new registrations of types.
            while (_queue.Any())
            {
                // process current batch of types.
                ProcessBatch(context, queryTypeName);

                // check if there are new types that have to be processed.
                EnqueueUnprocessedTypes(context.Types);
            }

            // add missing query type
            if (queryTypeName != null && !_registered.Contains(queryTypeName))
            {
                _queue.Enqueue(new ObjectType(d => d.Name(queryTypeName)));
                ProcessBatch(context, queryTypeName);
            }
        }

        private void ProcessBatch(
            ISchemaContext schemaContext,
            string queryTypeName)
        {
            while (_queue.Any())
            {
                INamedType type = _queue.Dequeue();
                if (!_registered.Contains(type.Name))
                {
                    _registered.Add(type.Name);
                    schemaContext.Types.RegisterType(type);
                    type = schemaContext.Types.GetType<INamedType>(type.Name);

                    RegisterTypeDependencies(schemaContext, type, queryTypeName);
                }
            }
        }

        // TODO : rename
        private void RegisterTypeDependencies(
            ISchemaContext schemaContext,
            INamedType type,
            string queryTypeName)
        {
            if (type is INeedsInitialization initializer)
            {
                bool isQueryType = string.Equals(queryTypeName,
                    type.Name, StringComparison.Ordinal);

                var initializationContext =
                    new TypeInitializationContext(schemaContext,
                        e => _errors.Add(e), type, isQueryType);

                initializer.RegisterDependencies(initializationContext);
            }
        }

        private void EnqueueUnprocessedTypes(ITypeRegistry types)
        {
            foreach (INamedType type in types.GetTypes())
            {
                if (!_registered.Contains(type.Name))
                {
                    _queue.Enqueue(type);
                }
            }
        }

        private void RegisterDirectiveDependencies(ISchemaContext schemaContext)
        {
            foreach (INeedsInitialization directive in schemaContext.Directives
                .GetDirectives().Cast<INeedsInitialization>())
            {
                var initializationContext =
                    new TypeInitializationContext(schemaContext,
                        e => _errors.Add(e));
                directive.RegisterDependencies(initializationContext);
            }
        }
    }
}
