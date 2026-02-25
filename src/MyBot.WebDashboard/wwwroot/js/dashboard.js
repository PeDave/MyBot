let currentTab = 'overview';
let allocationChart;

// Tab switching
function switchTab(tabName, btnEl) {
    currentTab = tabName;

    // Update buttons
    document.querySelectorAll('.tab-btn').forEach(btn => btn.classList.remove('active'));
    if (btnEl) btnEl.classList.add('active');

    // Update content
    document.querySelectorAll('.tab-content').forEach(content => content.classList.remove('active'));
    document.getElementById(`${tabName}-tab`).classList.add('active');

    // Load data
    if (tabName === 'overview') loadOverview();
    else if (tabName === 'exchanges') loadExchangesSummary();
    else if (tabName === 'details') loadExchangeDetails();
}

// 1️⃣ Overview Tab
async function loadOverview() {
    try {
        const response = await fetch('/api/portfolio/overview');
        const data = await response.json();

        // Total balance
        document.getElementById('totalBalance').textContent =
            `$${data.totalBalanceUsd.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;

        // Last updated
        const lastUpdated = new Date(data.lastUpdated);
        document.getElementById('lastUpdated').textContent =
            `Last updated: ${lastUpdated.toLocaleTimeString()}`;

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

        // Chart
        updateChart(data.coinBreakdown);

    } catch (error) {
        console.error('Failed to load overview:', error);
        document.getElementById('totalBalance').textContent = 'Error loading data';
    }
}

// 2️⃣ Exchanges Summary Tab
async function loadExchangesSummary() {
    try {
        const response = await fetch('/api/portfolio/exchanges');
        const data = await response.json();

        const container = document.getElementById('exchangesSummary');
        container.innerHTML = '';

        // Exchanges rendezése: USD érték szerint csökkenő
        const sortedExchanges = data.sort((a, b) => b.totalUsd - a.totalUsd);

        sortedExchanges.forEach(exchange => {
            let accountsHtml = '';

            // Account types rendezése: USD érték szerint csökkenő
            const sortedAccounts = Object.entries(exchange.accounts)
                .sort(([, a], [, b]) => b.totalUsd - a.totalUsd);

            sortedAccounts.forEach(([type, account]) => {
                // Aggregálás coin-onként
                const aggregated = {};
                account.balances.forEach(b => {
                    if (!aggregated[b.asset]) {
                        aggregated[b.asset] = { asset: b.asset, total: 0, usdValue: 0 };
                    }
                    aggregated[b.asset].total += b.free + b.locked;
                    aggregated[b.asset].usdValue += b.usdValue;
                });

                const uniqueAssets = Object.values(aggregated).filter(a => a.usdValue > 0);

                const assetsSummary = uniqueAssets
                    .slice(0, 3)
                    .map(a => `${a.asset}: ${a.total.toFixed(4)}`)
                    .join(', ');

                const moreText = uniqueAssets.length > 3 ? '...' : '';

                accountsHtml += `
                    <div class="account-type" onclick="toggleAssets(this)">
                        <span>${account.type}</span>
                        <span>$${account.totalUsd.toLocaleString('en-US', { minimumFractionDigits: 2 })}</span>
                    </div>
                    <div class="account-assets">
                        ${uniqueAssets.map(b => `
                            <div class="asset-row">
                                <span><strong>${b.asset}</strong></span>
                                <span>${b.total.toFixed(6)}</span>
                                <span class="coin-value">$${b.usdValue.toLocaleString('en-US', { minimumFractionDigits: 2 })}</span>
                            </div>
                        `).join('')}
                    </div>
                `;
            });

            container.innerHTML += `
                <div class="exchange-card">
                    <h3>
                        <span>${exchange.name}</span>
                        <span class="exchange-total">$${exchange.totalUsd.toLocaleString('en-US', { minimumFractionDigits: 2 })}</span>
                    </h3>
                    ${accountsHtml}
                </div>
            `;
        });

        // Populate exchange selector for details tab (rendezve)
        const select = document.getElementById('exchangeSelect');
        if (select.options.length === 0) {
            select.innerHTML = sortedExchanges.map(ex => `<option value="${ex.name}">${ex.name}</option>`).join('');
        }

    } catch (error) {
        console.error('Failed to load exchanges summary:', error);
    }
}

// 3️⃣ Exchange Details Tab
async function loadExchangeDetails() {
    const exchangeName = document.getElementById('exchangeSelect').value;
    if (!exchangeName) return;

    try {
        const response = await fetch(`/api/portfolio/exchange/${exchangeName}`);
        const data = await response.json();

        const container = document.getElementById('exchangeDetails');
        container.innerHTML = '';

        const accountTypes = [
            { key: 'spot', label: 'Spot Account' },
            { key: 'usdtMFutures', label: 'USDT-M Futures' },
            { key: 'coinMFutures', label: 'Coin-M Futures' },
            { key: 'futures', label: 'Futures' },
            { key: 'earn', label: 'Earn' },
            { key: 'bot', label: 'Bot' },
            { key: 'wealth', label: 'Wealth' },
            { key: 'unified', label: 'Unified' }
        ];

        accountTypes.forEach(({ key, label }) => {
            const balances = data.accounts[key];
            if (!balances || balances.length === 0) return;

            // AGGREGÁLÁS: Coin-onként összesítés (ha több azonos coin van)
            const aggregated = {};
            balances.forEach(b => {
                if (!aggregated[b.asset]) {
                    aggregated[b.asset] = {
                        asset: b.asset,
                        total: 0,
                        free: 0,
                        locked: 0,
                        usdValue: 0
                    };
                }
                aggregated[b.asset].total += b.free + b.locked;
                aggregated[b.asset].free += b.free;
                aggregated[b.asset].locked += b.locked;
                aggregated[b.asset].usdValue += b.usdValue;
            });

            // RENDEZÉS: USD érték szerint csökkenő sorrendben
            const uniqueBalances = Object.values(aggregated)
                .filter(b => b.usdValue > 0)
                .sort((a, b) => b.usdValue - a.usdValue);

            if (uniqueBalances.length === 0) return;

            const totalUsd = uniqueBalances.reduce((sum, b) => sum + b.usdValue, 0);

            let assetsHtml = uniqueBalances.map(b => `
                <div class="asset-row">
                    <span><strong>${b.asset}</strong></span>
                    <span>${b.total.toFixed(6)}</span>
                    <span class="coin-value">$${b.usdValue.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}</span>
                    <span><small>Free: ${b.free.toFixed(6)} | Locked: ${b.locked.toFixed(6)}</small></span>
                </div>
            `).join('');

            container.innerHTML += `
                <div class="account-section">
                    <h4>${label} — Total: $${totalUsd.toLocaleString('en-US', { minimumFractionDigits: 2 })}</h4>
                    ${assetsHtml}
                </div>
            `;
        });

        if (container.innerHTML === '') {
            container.innerHTML = '<p style="opacity:0.7">No balances found for this exchange.</p>';
        }

    } catch (error) {
        console.error('Failed to load exchange details:', error);
    }
}

// Helper: Toggle asset details in Exchange Summary tab
function toggleAssets(element) {
    const assets = element.nextElementSibling;
    assets.classList.toggle('expanded');
}

// Helper: Update doughnut chart with coin allocation
function updateChart(coinBreakdown) {
    const ctx = document.getElementById('allocationChart').getContext('2d');

    if (allocationChart) allocationChart.destroy();

    const sortedCoins = Object.values(coinBreakdown)
        .sort((a, b) => b.usdValue - a.usdValue)
        .slice(0, 10); // Top 10

    allocationChart = new Chart(ctx, {
        type: 'doughnut',
        data: {
            labels: sortedCoins.map(c => c.asset),
            datasets: [{
                data: sortedCoins.map(c => c.usdValue),
                backgroundColor: [
                    '#4CAF50', '#2196F3', '#FF9800', '#9C27B0', '#F44336',
                    '#00BCD4', '#FFEB3B', '#795548', '#607D8B', '#E91E63'
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
                    labels: { color: '#fff', font: { size: 14 } }
                }
            }
        }
    });
}

// Refresh current tab
function refreshAllTabs() {
    if (currentTab === 'overview') loadOverview();
    else if (currentTab === 'exchanges') loadExchangesSummary();
    else if (currentTab === 'details') loadExchangeDetails();
}

// Auto-refresh every 30 seconds
setInterval(refreshAllTabs, 30000);

// Initial load
loadOverview();
