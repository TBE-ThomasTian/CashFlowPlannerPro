#include "Dashboard.h"
#include "Database.h"
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
#include <QtCharts/QChartView>
#include <QtCharts/QChart>
#include <QtCharts/QBarSet>
#include <QtCharts/QBarSeries>
#include <QtCharts/QBarCategoryAxis>
#include <QtCharts/QValueAxis>
#include <cmath>

static double npv(const QVector<double>& s,double r){ 
    if(r==0){
        double t=0;
        for(double v:s)t+=v;
        return t;
    } 
    double rm=std::pow(1.0+r/100.0,1.0/12.0)-1.0; 
    double t=0; 
    for(int i=0;i<s.size();++i) t+= s[i]/std::pow(1.0+rm,i); 
    return t; 
}

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
    m_includeInvoices = new QCheckBox("Offene Rechnungen");
    m_includeInvoices->setChecked(true);
    m_includeInvoices->setToolTip("Berücksichtigt offene Rechnungen am Fälligkeitsdatum");
    
    m_includeOffers = new QCheckBox("Angebote");
    m_includeOffers->setChecked(true);
    m_includeOffers->setToolTip("Berücksichtigt Angebote mit ihrer Wahrscheinlichkeit");
    
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
    controlsRow->addWidget(new QLabel("<b>Berücksichtigen:</b>"));
    controlsRow->addWidget(m_includeInvoices);
    controlsRow->addWidget(m_includeOffers);
    controlsRow->addWidget(m_includeRecurring);
    controlsRow->addStretch(); 
    controlsRow->addWidget(btnSave); 
    controlsRow->addWidget(btnRefresh);
    
    root->addLayout(controlsRow);
    
    // Chart - NO EFFECTS
    m_chart=new QChartView(this); 
    m_chart->setMinimumHeight(300);
    root->addWidget(m_chart, 2);
    
    // Table
    m_table=new QTableWidget(0, 5, this); 
    m_table->setHorizontalHeaderLabels({"Monat", "Netto", "Kumuliert", "Ziel", "Abweichung"}); 
    m_table->horizontalHeader()->setStretchLastSection(true);
    m_table->setAlternatingRowColors(true);
    m_table->setMinimumHeight(200);
    root->addWidget(m_table, 3);
    
    // Connect signals
    connect(btnRefresh, &QPushButton::clicked, this, &Dashboard::refresh); 
    connect(btnSave, &QPushButton::clicked, this, &Dashboard::saveStartBalance);
    connect(m_rate, QOverload<double>::of(&QDoubleSpinBox::valueChanged), this, &Dashboard::refresh);
    connect(m_horizon, QOverload<int>::of(&QSpinBox::valueChanged), this, &Dashboard::refresh);
    connect(m_includeInvoices, &QCheckBox::toggled, this, &Dashboard::refresh);
    connect(m_includeOffers, &QCheckBox::toggled, this, &Dashboard::refresh);
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
    bool includeOffers = m_includeOffers->isChecked();
    bool includeInvoices = m_includeInvoices->isChecked();
    bool includeRecurring = m_includeRecurring->isChecked();
    
    auto rows=Database::instance().monthlyCashflow(horizon, includeOffers, includeInvoices, includeRecurring); 
    auto tmap=Database::instance().targets();
    
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
        
        m_table->setItem(i, 0, new QTableWidgetItem(rows[i].ym));
        m_table->setItem(i, 1, new QTableWidgetItem(QString::number(rows[i].net, 'f', 2)));
        m_table->setItem(i, 2, new QTableWidgetItem(QString::number(run, 'f', 2)));
        m_table->setItem(i, 3, new QTableWidgetItem(QString::number(target, 'f', 2)));
        
        auto*varItem = new QTableWidgetItem(QString::number(var, 'f', 2)); 
        varItem->setForeground(var < 0 ? QColor("#EF4444") : QColor("#10B981")); 
        m_table->setItem(i, 4, varItem);
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
    
    // Create new chart
    auto* chart = new QChart(); 
    
    // Simple white background
    chart->setBackgroundBrush(QBrush(QColor(255, 255, 255)));
    chart->setPlotAreaBackgroundVisible(false);
    
    // Create bar series
    auto* set = new QBarSet("Cashflow"); 
    for(double v: series) {
        *set << v;
    }
    
    // Use the same blue as in the design
    set->setColor(QColor(100, 132, 154));  // #64849a
    set->setBorderColor(QColor(100, 132, 154));
    
    auto* barSeries = new QBarSeries(); 
    barSeries->append(set); 
    chart->addSeries(barSeries);
    
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
    
    // Simple title
    chart->setTitle("Monatlicher Cashflow");
    chart->setTitleBrush(QBrush(QColor(0, 0, 0)));
    chart->setTitleFont(QFont("Arial", 14));
    chart->legend()->setVisible(false);
    
    // Apply chart
    m_chart->setChart(chart);
}