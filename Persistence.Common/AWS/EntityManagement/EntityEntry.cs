using Persistence.Common.AWS.Builder;

namespace Persistence.Common.AWS.EntityManagement
{

    public class EntityEntry
    {
        public object Entity { get; }
        public Type EntityType { get; }
        public PropertyValues OriginalValues { get; private set; }
        public EntityState State { get; set; }

        public EntityEntry(object entity, EntityState state)
        {
            Entity = entity;
            EntityType = entity.GetType();
            State = state;
            OriginalValues = CaptureOriginalValues(entity);
        }

        // Capture original values as a snapshot without deep cloning complex types
        private PropertyValues CaptureOriginalValues(object entity)
        {
            var propertyValues = new PropertyValues(EntityType);
            var dynamoDBEntityBuilder = InMemoryDynamoDBEntitiesConfiguration.GetConfiguration(EntityType);
            propertyValues.Capture(entity, dynamoDBEntityBuilder?.GetPropertyConfigurations(), dynamoDBEntityBuilder?.GetNestedEntityConfigurations()); // Capture current property values as a snapshot
            return propertyValues;
        }

        // Method to check if the entity has changes by comparing OriginalValues with current entity values
        public bool HasChanges()
        {
            var currentValues = new PropertyValues(EntityType);
            var dynamoDBEntityBuilder = InMemoryDynamoDBEntitiesConfiguration.GetConfiguration(EntityType);
            currentValues.Capture(Entity, dynamoDBEntityBuilder?.GetPropertyConfigurations(), dynamoDBEntityBuilder?.GetNestedEntityConfigurations());
            return OriginalValues.HasChanges(currentValues);
        }

        public void ResetAfterSave()
        {
            // Capture the current state as the new "original" state
            OriginalValues = CaptureOriginalValues(Entity);

            // Set the entity state to Unchanged, similar to EF behavior after a save
            State = EntityState.Unchanged;
        }
    }
}
