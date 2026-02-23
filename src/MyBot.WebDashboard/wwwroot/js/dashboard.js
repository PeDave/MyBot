let allocationChart;

async function refreshData() {
    try {
        const response = await fetch('/api/portfolio');
        const data = await response.json();

        // Total balance
        document.getElementById('totalBalance').textContent =
            `$${data.totalBalanceUsd.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;

        // Last updated
        const lastUpdated = new Date(data.lastUpdated);
        document.getElementById('lastUpdated').textContent =
            `Last updated: ${lastUpdated.toLocaleTimeString()}`;

        // Exchanges table
        const tbody = document.getElementById('exchangesBody');
        tbody.innerHTML = '';
        data.exchanges.forEach(ex => {
            const share = data.totalBalanceUsd > 0
                ? ((ex.totalUsd / data.totalBalanceUsd) * 100).toFixed(1)
                : '0.0';
            tbody.innerHTML += `
                <tr>
                    <td>${ex.name}</td>
                    <td>$${ex.totalUsd.toLocaleString('en-US', { minimumFractionDigits: 2 })}</td>
                    <td>${share}%</td>
                </tr>
            `;
        });

        // Allocation chart
        updateChart(data.exchanges);

        // Coin breakdown
        const coinList = document.getElementById('coinList');
        coinList.innerHTML = '';

        const sortedCoins = Object.values(data.coinBreakdown)
            .sort((a, b) => b.usdValue - a.usdValue);

        sortedCoins.forEach(coin => {
            coinList.innerHTML += `
                <div class="coin-item">
                    <span class="coin-name">${coin.asset}</span>
                    <span>${coin.totalQuantity.toFixed(6)}</span>
                    <span class="coin-value">$${coin.usdValue.toLocaleString('en-US', { minimumFractionDigits: 2 })}</span>
                </div>
            `;
        });

    } catch (error) {
        console.error('Failed to fetch portfolio data:', error);
        document.getElementById('totalBalance').textContent = 'Error loading data';
    }
}

function updateChart(exchanges) {
    const ctx = document.getElementById('allocationChart').getContext('2d');

    if (allocationChart) {
        allocationChart.destroy();
    }

    allocationChart = new Chart(ctx, {
        type: 'doughnut',
        data: {
            labels: exchanges.map(e => e.name),
            datasets: [{
                data: exchanges.map(e => e.totalUsd),
                backgroundColor: [
                    '#4CAF50',
                    '#2196F3',
                    '#FF9800',
                    '#9C27B0',
                    '#F44336'
                ],
                borderWidth: 0
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: true,
            plugins: {
                legend: {
                    position: 'bottom',
                    labels: {
                        color: '#fff',
                        font: {
                            size: 14
                        }
                    }
                }
            }
        }
    });
}

// Auto-refresh every 30 seconds
setInterval(refreshData, 30000);

// Initial load
refreshData();
