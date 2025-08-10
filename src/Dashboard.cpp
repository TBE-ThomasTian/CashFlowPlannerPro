#include "Dashboard.h"
#include "Database.h"
#include <QSqlQuery>
#include <QDate>
#include <QDebug>
#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QGridLayout>
#include <QLabel>
#include <QTableWidget>
#include <QTableWidgetItem>
#include <QDoubleSpinBox>
#include <QSpinBox>
#include <QPushButton>
#include <QHeaderView>
#include <QFrame>
#include <QLocale>
#include <QCheckBox>
#include <QSplitter>
#include <QtCharts/QChartView>
#include <QtCharts/QChart>
#include <QtCharts/QBarSet>
#include <QtCharts/QBarSeries>
#include <QtCharts/QBarCategoryAxis>
#include <QtCharts/QValueAxis>
#include <QtCharts/QLineSeries>
#include <cmath>


Dashboard::Dashboard(QWidget*parent):QWidget(parent){
    // DISABLE ALL EFFECTS
    
    // Main layout
    auto*root=new QVBoxLayout(this); 
    root->setSpacing(16);
    root->setContentsMargins(24, 24, 24, 24);
    
    // Info text
    auto*infoLabel = new QLabel("<b>So funktioniert's:</b> Geben Sie Ihren aktuellen Kontostand ein. "
                                "Unter 'Ein/Ausgaben' tragen Sie Ihre monatlichen Einnahmen (Gehalt, etc.) als positive Beträge "
                                "und Ausgaben (Miete, etc.) als negative Beträge ein. Die Prognose zeigt, wie sich Ihr Konto entwickelt.");
    infoLabel->setWordWrap(true);
    infoLabel->setStyleSheet("QLabel { background-color: #f0f0f0; padding: 10px; border-radius: 5px; }");
    root->addWidget(infoLabel);
    
    // KPI Cards Row
    auto*kpiRow=new QHBoxLayout(); 
    kpiRow->setSpacing(16);
    
    // Initialize KPI labels
    m_sum=new QLabel("€ 0.00"); 
    m_npv=new QLabel("€ 0.00"); 
    m_irr=new QLabel("—");
    m_offersSum=new QLabel("€ 0.00");
    m_invoicesSum=new QLabel("€ 0.00");
    m_burnRate=new QLabel("€ 0.00");
    m_runway=new QLabel("—");
    
    // Create modern KPI card function with icon space
    auto createCard=[](const QString&title, QLabel*value, const QString&iconText, bool isDark = false) -> QWidget* { 
        auto*card=new QFrame(); 
        card->setObjectName(isDark ? "CardDark" : "Card");
        card->setMinimumHeight(120);
        card->setMaximumHeight(140);
        card->setMinimumWidth(180);
        
        // Use grid layout for better control
        auto*layout=new QGridLayout(card); 
        layout->setSpacing(8);
        layout->setContentsMargins(20, 20, 20, 20);
        
        // Title with icon on the right
        auto*topRow = new QHBoxLayout();
        auto*titleLabel=new QLabel(title); 
        titleLabel->setObjectName("CardTitle");
        
        auto*iconLabel = new QLabel(iconText);
        iconLabel->setObjectName("CardIcon");
        iconLabel->setAlignment(Qt::AlignRight);
        
        topRow->addWidget(titleLabel);
        topRow->addStretch();
        topRow->addWidget(iconLabel);
        
        // Value
        value->setObjectName(isDark ? "CardValueDark" : "CardValue");
        value->setAlignment(Qt::AlignLeft | Qt::AlignVCenter);
        
        layout->addLayout(topRow, 0, 0);
        layout->addWidget(value, 1, 0);
        layout->setRowStretch(1, 1);
        
        return card; 
    };
    
    // Add KPI cards with icons
    kpiRow->addWidget(createCard("Kontostand Heute", m_sum, "💰", true));  // Dark card for current balance
    kpiRow->addWidget(createCard("Prognose Ende", m_npv, "📊", false));  // End balance after forecast period
    kpiRow->addWidget(createCard("Monatl. Cashflow", m_irr, "📈", false));  // Average monthly cashflow
    kpiRow->addWidget(createCard("Aktive Angebote", m_offersSum, "🎯", false));  // Active offers sum
    kpiRow->addWidget(createCard("Offene Rechnungen", m_invoicesSum, "📋", false));  // Open invoices sum
    kpiRow->addWidget(createCard("Burn Rate", m_burnRate, "🔥", false));  // Monthly burn rate
    kpiRow->addWidget(createCard("Runway", m_runway, "⏱️", false));  // Months until money runs out
    kpiRow->addStretch();
    
    root->addLayout(kpiRow);
    
    // Controls Row
    auto*controlsRow=new QHBoxLayout(); 
    controlsRow->setSpacing(12);
    
    // Create input controls with German labels
    m_startBalance=new QDoubleSpinBox(); 
    m_startBalance->setPrefix("€ ");
    m_startBalance->setRange(-1000000, 1000000); 
    m_startBalance->setDecimals(2);
    m_startBalance->setSingleStep(100.0);
    m_startBalance->setValue(Database::instance().settingStartBalance());
    m_startBalance->setMinimumWidth(180);
    m_startBalance->setToolTip("Ihr aktueller Kontostand");
    m_startBalance->setButtonSymbols(QAbstractSpinBox::UpDownArrows);  // Force arrows
    
    m_horizon=new QSpinBox(); 
    m_horizon->setSuffix(" Monate");
    m_horizon->setRange(1,60); 
    m_horizon->setValue(12);
    m_horizon->setMinimumWidth(120);
    m_horizon->setToolTip("Wie viele Monate in die Zukunft berechnen");
    m_horizon->setButtonSymbols(QAbstractSpinBox::UpDownArrows);  // Force arrows
    
    // Hidden rate field (still needed for NPV calculation but not shown)
    m_rate=new QDoubleSpinBox(); 
    m_rate->setValue(0.0);
    m_rate->setHidden(true);
    
    // Create checkboxes for including different items
    m_includeInvoices = new QCheckBox("Rechnungen");
    m_includeInvoices->setChecked(true);
    m_includeInvoices->setToolTip("Berücksichtigt offene Rechnungen am Fälligkeitsdatum");
    
    m_includeOffers = new QCheckBox("Offene Angebote");
    m_includeOffers->setChecked(true);
    m_includeOffers->setToolTip("Berücksichtigt offene Angebote mit ihrer Wahrscheinlichkeit");
    
    m_includeOffersBeauftragt = new QCheckBox("Beauftragte");
    m_includeOffersBeauftragt->setChecked(true);
    m_includeOffersBeauftragt->setToolTip("Berücksichtigt beauftragte Angebote mit ihrer Wahrscheinlichkeit");
    
    m_includeRecurring = new QCheckBox("Wiederkehrend");
    m_includeRecurring->setChecked(true);
    m_includeRecurring->setToolTip("Berücksichtigt wiederkehrende Zahlungen (Fixkosten, Steuern)");
    
    // Create buttons
    auto*btnRefresh=new QPushButton("Aktualisieren"); 
    auto*btnSave=new QPushButton("Kontostand speichern");
    btnSave->setObjectName("SecondaryButton");
    
    // Add controls to layout with clear German labels
    controlsRow->addWidget(new QLabel("<b>Aktueller Kontostand:</b>")); 
    controlsRow->addWidget(m_startBalance);
    controlsRow->addWidget(new QLabel("<b>Prognose für:</b>")); 
    controlsRow->addWidget(m_horizon);
    
    // Add separator
    auto*separator1 = new QFrame();
    separator1->setFrameShape(QFrame::VLine);
    separator1->setFrameShadow(QFrame::Sunken);
    controlsRow->addWidget(separator1);
    
    // Add checkboxes to the same row
    controlsRow->addWidget(new QLabel("<b>Berücksichtigen:</b>"));
    controlsRow->addWidget(m_includeInvoices);
    controlsRow->addWidget(m_includeOffers);
    controlsRow->addWidget(m_includeOffersBeauftragt);
    controlsRow->addWidget(m_includeRecurring);
    
    controlsRow->addStretch(); 
    controlsRow->addWidget(btnSave); 
    controlsRow->addWidget(btnRefresh);
    
    root->addLayout(controlsRow);
    
    // Create main vertical splitter for all sections
    auto*mainSplitter = new QSplitter(Qt::Vertical, this);
    mainSplitter->setChildrenCollapsible(false);
    
    // Container for KPI cards
    auto*kpiContainer = new QWidget();
    auto*kpiContainerLayout = new QVBoxLayout(kpiContainer);
    kpiContainerLayout->setContentsMargins(0, 0, 0, 0);
    kpiContainerLayout->addLayout(kpiRow);
    
    // Add KPI container to main splitter
    mainSplitter->addWidget(kpiContainer);
    
    // Chart - NO EFFECTS
    m_chart=new QChartView(this); 
    m_chart->setMinimumHeight(200);
    mainSplitter->addWidget(m_chart);
    
    // Table
    m_table=new QTableWidget(0, 6, this); 
    m_table->setHorizontalHeaderLabels({"Monat", "Netto", "Kumuliert", "Ziel", "Abweichung", "Rechnungen"}); 
    m_table->horizontalHeader()->setStretchLastSection(true);
    m_table->setAlternatingRowColors(true);
    m_table->setMinimumHeight(150);
    mainSplitter->addWidget(m_table);
    
    // Set initial splitter sizes (20% KPI, 45% chart, 35% table)
    mainSplitter->setStretchFactor(0, 1);
    mainSplitter->setStretchFactor(1, 2);
    mainSplitter->setStretchFactor(2, 2);
    
    root->addWidget(mainSplitter, 1);
    
    // Connect signals
    connect(btnRefresh, &QPushButton::clicked, this, &Dashboard::refresh); 
    connect(btnSave, &QPushButton::clicked, this, &Dashboard::saveStartBalance);
    connect(m_rate, QOverload<double>::of(&QDoubleSpinBox::valueChanged), this, &Dashboard::refresh);
    connect(m_horizon, QOverload<int>::of(&QSpinBox::valueChanged), this, &Dashboard::refresh);
    connect(m_includeInvoices, &QCheckBox::toggled, this, &Dashboard::refresh);
    connect(m_includeOffers, &QCheckBox::toggled, this, &Dashboard::refresh);
    connect(m_includeOffersBeauftragt, &QCheckBox::toggled, this, &Dashboard::refresh);
    connect(m_includeRecurring, &QCheckBox::toggled, this, &Dashboard::refresh);
    
    // Initial refresh
    refresh();
}

void Dashboard::saveStartBalance(){ 
    Database::instance().setSettingStartBalance(m_startBalance->value()); 
    refresh(); 
}

void Dashboard::refresh(){
    int horizon=m_horizon->value(); 
    bool includeOffersOffen = m_includeOffers->isChecked();
    bool includeOffersBeauftragt = m_includeOffersBeauftragt->isChecked();
    bool includeInvoices = m_includeInvoices->isChecked();
    bool includeRecurring = m_includeRecurring->isChecked();
    
    qDebug() << "Dashboard refresh - OffersOffen:" << includeOffersOffen << "OffersBeauftragt:" << includeOffersBeauftragt;
    
    auto rows=Database::instance().monthlyCashflow(horizon, includeOffersOffen, includeOffersBeauftragt, includeInvoices, includeRecurring); 
    auto tmap=Database::instance().targets();
    
    // Get monthly invoice data
    QMap<QString, double> monthlyInvoices;
    QSqlQuery invoiceQuery(Database::instance().db());
    if(invoiceQuery.exec("SELECT due_date, amount FROM invoices WHERE status = 'Offen' OR status IS NULL")) {
        while(invoiceQuery.next()) {
            QString dueStr = invoiceQuery.value(0).toString();
            QDate dueDate = QDate::fromString(dueStr, Qt::ISODate);
            if(!dueDate.isValid()) {
                dueDate = QDate::fromString(dueStr, "dd.MM.yyyy");
            }
            if(!dueDate.isValid()) {
                dueDate = QDate::fromString(dueStr, "yyyy-MM-dd");
            }
            if(dueDate.isValid()) {
                QString monthKey = QString("%1-%2").arg(dueDate.year()).arg(dueDate.month(), 2, 10, QChar('0'));
                monthlyInvoices[monthKey] += invoiceQuery.value(1).toDouble();
            }
        }
    }
    
    m_table->setRowCount(rows.size()); 
    QVector<double> series; 
    series.reserve(rows.size()); 
    QVector<double> cum; 
    cum.reserve(rows.size());
    
    double sum=0, run=m_startBalance->value(); 
    QStringList labels;
    
    for(int i=0; i<rows.size(); ++i){ 
        labels << rows[i].ym; 
        series << rows[i].net; 
        sum += rows[i].net; 
        run += rows[i].net; 
        cum << run;
        
        double target = tmap.value(rows[i].ym, 0.0); 
        double var = rows[i].net - target;
        double invoiceAmount = monthlyInvoices.value(rows[i].ym, 0.0);
        
        m_table->setItem(i, 0, new QTableWidgetItem(rows[i].ym));
        m_table->setItem(i, 1, new QTableWidgetItem(QString::number(rows[i].net, 'f', 2) + " €"));
        m_table->setItem(i, 2, new QTableWidgetItem(QString::number(run, 'f', 2) + " €"));
        m_table->setItem(i, 3, new QTableWidgetItem(QString::number(target, 'f', 2) + " €"));
        
        auto*varItem = new QTableWidgetItem(QString::number(var, 'f', 2) + " €"); 
        varItem->setForeground(var < 0 ? QColor("#EF4444") : QColor("#10B981")); 
        m_table->setItem(i, 4, varItem);
        
        // Add invoice column with green color for positive amounts
        auto*invoiceItem = new QTableWidgetItem(QString::number(invoiceAmount, 'f', 2) + " €");
        if(invoiceAmount > 0) {
            invoiceItem->setForeground(QColor("#10B981")); // Green for income
        }
        m_table->setItem(i, 5, invoiceItem);
    }
    
    // Update KPIs with meaningful values
    double currentBalance = m_startBalance->value();
    double endBalance = run;  // This is the final cumulative value
    double avgMonthly = series.size() > 0 ? sum / series.size() : 0;
    
    // Find when money runs out
    int monthsUntilZero = -1;
    for(int i = 0; i < cum.size(); ++i) {
        if(cum[i] <= 0) {
            monthsUntilZero = i + 1;
            break;
        }
    }
    
    // Format with thousand separators
    QLocale locale(QLocale::German);
    m_sum->setText(locale.toString(currentBalance, 'f', 2) + " €");  // Current balance
    
    // Show warning if money runs out
    if(monthsUntilZero > 0) {
        m_npv->setText(QString("<span style='color: red;'>Geld alle in %1 Monaten!</span>").arg(monthsUntilZero));
    } else {
        m_npv->setText(locale.toString(endBalance, 'f', 2) + " €");      // End balance after forecast
    }
    
    m_irr->setText(locale.toString(avgMonthly, 'f', 2) + " €");      // Average monthly cashflow
    
    // Calculate Burn Rate (average monthly expenses) and Runway
    double totalExpenses = 0.0;
    double totalIncome = 0.0;
    int monthCount = 0;
    
    for(const auto& row : rows) {
        if(row.net < 0) {
            totalExpenses += std::abs(row.net);
        } else {
            totalIncome += row.net;
        }
        monthCount++;
    }
    
    double avgMonthlyExpenses = monthCount > 0 ? totalExpenses / monthCount : 0;
    double netBurnRate = monthCount > 0 ? (totalExpenses - totalIncome) / monthCount : 0;
    
    // Calculate runway (how many months until money runs out)
    double runway = 0;
    if(netBurnRate > 0 && currentBalance > 0) {
        runway = currentBalance / netBurnRate;
    } else if(netBurnRate <= 0) {
        runway = -1; // Infinite runway (profitable)
    }
    
    // Display Burn Rate (show gross expenses, more useful)
    m_burnRate->setText(locale.toString(-avgMonthlyExpenses, 'f', 2) + " €/Monat");
    
    // Display Runway
    if(runway < 0) {
        m_runway->setText("<span style='color: green;'>∞ (Profitabel)</span>");
    } else if(runway == 0) {
        m_runway->setText("<span style='color: red;'>0 Monate</span>");
    } else if(runway < 6) {
        m_runway->setText(QString("<span style='color: red;'>%1 Monate</span>").arg(QString::number(runway, 'f', 1)));
    } else if(runway < 12) {
        m_runway->setText(QString("<span style='color: orange;'>%1 Monate</span>").arg(QString::number(runway, 'f', 1)));
    } else {
        m_runway->setText(QString("<span style='color: green;'>%1 Monate</span>").arg(QString::number(runway, 'f', 1)));
    }
    
    // Update offers and invoices sums
    // Calculate offers sum based on checkbox state
    double offersSum = 0.0;
    if(includeOffersOffen || includeOffersBeauftragt) {
        QSqlQuery q(Database::instance().db());
        QString statusCondition;
        if(includeOffersOffen && includeOffersBeauftragt){
            statusCondition = "WHERE status = 'Offen' OR status = 'Beauftragt' OR status IS NULL";
        } else if(includeOffersOffen){
            statusCondition = "WHERE status = 'Offen' OR status IS NULL";
        } else if(includeOffersBeauftragt){
            statusCondition = "WHERE status = 'Beauftragt'";
        }
        QString query = QString("SELECT amount, probability FROM offers %1").arg(statusCondition);
        if(q.exec(query)){
            while(q.next()){
                double amt = q.value(0).toDouble();
                double prob = q.value(1).toDouble();
                if(prob > 1.0) prob /= 100.0;
                // Include full amount if probability > 0%
                if(prob > 0) {
                    offersSum += amt; // Use full amount, not weighted
                }
            }
        }
    }
    
    double invoicesSum = Database::instance().openInvoicesSum();
    m_offersSum->setText(locale.toString(offersSum, 'f', 2) + " €");
    m_invoicesSum->setText(locale.toString(invoicesSum, 'f', 2) + " €");
    
    // Create new chart
    auto* chart = new QChart(); 
    
    // Simple white background
    chart->setBackgroundBrush(QBrush(QColor(255, 255, 255)));
    chart->setPlotAreaBackgroundVisible(false);
    
    // Create bar series for monthly cashflow
    auto* set = new QBarSet("Monatlicher Cashflow"); 
    for(double v: series) {
        *set << v;
    }
    
    // Use blue for bars
    set->setColor(QColor(100, 132, 154));  // #64849a
    set->setBorderColor(QColor(100, 132, 154));
    
    auto* barSeries = new QBarSeries(); 
    barSeries->append(set); 
    chart->addSeries(barSeries);
    
    // Create line series for cumulative balance
    auto* lineSeries = new QLineSeries();
    lineSeries->setName("Kontostand");
    for(int i = 0; i < cum.size(); ++i) {
        lineSeries->append(i, cum[i]);
    }
    
    // Style the line
    QPen pen(QColor(220, 38, 127));  // Pink/red color for line
    pen.setWidth(3);
    lineSeries->setPen(pen);
    
    // Add markers to the line
    lineSeries->setPointsVisible(true);
    lineSeries->setPointLabelsVisible(false);
    
    chart->addSeries(lineSeries);
    
    // Add horizontal zero line
    auto* zeroLine = new QLineSeries();
    zeroLine->setName("Nulllinie");
    zeroLine->append(-0.5, 0);
    zeroLine->append(labels.size() - 0.5, 0);
    
    // Style the zero line
    QPen zeroPen(QColor(120, 120, 120));  // Gray color for zero line
    zeroPen.setWidth(2);
    zeroPen.setStyle(Qt::DashLine);  // Dashed line
    zeroLine->setPen(zeroPen);
    zeroLine->setPointsVisible(false);
    
    chart->addSeries(zeroLine);
    
    // X Axis - simple
    auto* axisX = new QBarCategoryAxis(); 
    axisX->append(labels); 
    axisX->setLabelsColor(QColor(0, 0, 0));
    axisX->setGridLineVisible(true);
    
    // Y Axis - simple 
    auto* axisY = new QValueAxis(); 
    axisY->setLabelsColor(QColor(0, 0, 0));
    axisY->setGridLineColor(QColor(200, 200, 200));
    
    chart->addAxis(axisX, Qt::AlignBottom); 
    chart->addAxis(axisY, Qt::AlignLeft); 
    barSeries->attachAxis(axisX); 
    barSeries->attachAxis(axisY);
    lineSeries->attachAxis(axisX);
    lineSeries->attachAxis(axisY);
    zeroLine->attachAxis(axisX);
    zeroLine->attachAxis(axisY);
    
    // Simple title
    chart->setTitle("Cashflow & Kontostand");
    chart->setTitleBrush(QBrush(QColor(0, 0, 0)));
    chart->setTitleFont(QFont("Arial", 14));
    
    // Show legend for both series
    chart->legend()->setVisible(true);
    chart->legend()->setAlignment(Qt::AlignBottom);
    chart->legend()->setFont(QFont("Arial", 10));
    
    // Apply chart
    m_chart->setChart(chart);
}