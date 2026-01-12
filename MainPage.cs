using Microsoft.Maui.Controls;

namespace thesis_2
{
    public partial class MainPage : ContentPage
    {
        public MainPage()
        {
            InitializeComponent();
        }

        // Event handler for "All Appliances" button click
        private async void OnAllAppliancesClicked(object sender, EventArgs e)
        {
            // Fetch data from Entry fields and do something with it (e.g., save or display it)
            string fridgeName = FridgeNameEntry.Text;
            double fridgeUsage = double.TryParse(FridgeUsageLabel.Text, out var usage) ? usage : 0;
            double fridgeCost = double.TryParse(FridgeCostLabel.Text, out var cost) ? cost : 0;

            string acName = ACNameEntry.Text;
            double acUsage = double.TryParse(ACUsageLabel.Text, out var acEnergy) ? acEnergy : 0;
            double acCost = double.TryParse(ACCostEntry.Text, out var acPrice) ? acPrice : 0; // Fixed line

            // Show the updated appliance data
            await DisplayAlert("Updated Appliances",
                $"Fridge: {fridgeName}, Usage: {fridgeUsage} kWh, Cost: ?{fridgeCost}\n" +
                $"AC: {acName}, Usage: {acUsage} kWh, Cost: ?{acCost}",
                "OK");

            // Optionally, you can save these values to a database or perform further processing.
            // SaveApplianceData(fridgeName, fridgeUsage, fridgeCost, acName, acUsage, acCost);
        }

        // Event handler for "View Logs" button click
        private async void OnViewLogsClicked(object sender, EventArgs e)
        {
            await DisplayAlert("Logs", "Here you can view all your electricity usage logs.", "OK");
        }

        // Example of a method to save appliance data (optional)
        private void SaveApplianceData(string fridgeName, double fridgeUsage, double fridgeCost, string acName, double acUsage, double acCost)
        {
            // You can implement the logic to save data to a local database or cloud storage here.
        }
    }
}
