#include "ResourcesPage.h"
#include "Database.h"
#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QTableWidget>
#include <QPushButton>
#include <QComboBox>
#include <QDateEdit>
#include <QLabel>
#include <QScrollArea>
#include <QHeaderView>
#include <QPainter>
#include <QMouseEvent>
#include <QDrag>
#include <QMimeData>
#include <QInputDialog>
#include <QColorDialog>
#include <QMessageBox>
#include <QMenu>
#include <QAction>
#include <QSqlQuery>
#include <QSqlError>

ResourcesPage::ResourcesPage(QWidget* parent) : QWidget(parent), currentDate(QDate::currentDate()) {
    setupUI();
    loadResources();
    loadProjects();
    updateCalendar();
}

void ResourcesPage::setupUI() {
    mainLayout = new QVBoxLayout(this);
    mainLayout->setContentsMargins(0, 0, 0, 0);
    mainLayout->setSpacing(0);
    
    auto* headerWidget = new QWidget();
    headerWidget->setObjectName("headerWidget");
    headerWidget->setStyleSheet("QWidget#headerWidget { padding: 10px; }");
    headerLayout = new QHBoxLayout(headerWidget);
    
    btnPrevious = new QPushButton("◀");
    btnNext = new QPushButton("▶");
    btnToday = new QPushButton("Heute");
    lblDateRange = new QLabel();
    lblDateRange->setStyleSheet("font-size: 16px; font-weight: bold;");
    
    cmbViewMode = new QComboBox();
    cmbViewMode->addItems({"Tag", "Woche", "Monat"});
    cmbViewMode->setCurrentIndex(1);
    cmbViewMode->setStyleSheet("QComboBox { padding: 5px 10px; }");
    
    headerLayout->addWidget(btnPrevious);
    headerLayout->addWidget(btnToday);
    headerLayout->addWidget(btnNext);
    headerLayout->addSpacing(20);
    headerLayout->addWidget(lblDateRange);
    headerLayout->addStretch();
    headerLayout->addWidget(new QLabel("Ansicht:"));
    headerLayout->addWidget(cmbViewMode);
    
    auto* contentWidget = new QWidget();
    contentLayout = new QHBoxLayout(contentWidget);
    contentLayout->setContentsMargins(0, 0, 0, 0);
    contentLayout->setSpacing(0);
    
    auto* sidebarWidget = new QWidget();
    sidebarWidget->setObjectName("sidebarWidget");
    sidebarWidget->setStyleSheet("QWidget#sidebarWidget { background: #ecf0f1; border-right: 1px solid #bdc3c7; }");
    sidebarWidget->setFixedWidth(250);
    sidebarLayout = new QVBoxLayout(sidebarWidget);
    
    auto* sidebarTitle = new QLabel("Projekte");
    sidebarTitle->setStyleSheet("font-size: 14px; font-weight: bold; padding: 10px;");
    sidebarLayout->addWidget(sidebarTitle);
    
    auto* helpLabel = new QLabel("📌 Projekt aus Liste auf Mitarbeiter/Tag ziehen\n⚡ Mit Shift: ganze Woche\n🗑️ Rechtsklick zum Löschen");
    helpLabel->setStyleSheet("font-size: 11px; color: #7f8c8d; padding: 5px; background: #f8f9fa; border-radius: 3px; margin: 5px;");
    helpLabel->setWordWrap(true);
    sidebarLayout->addWidget(helpLabel);
    
    btnAddResource = new QPushButton("+ Mitarbeiter hinzufügen");
    btnAddProject = new QPushButton("+ Projekt hinzufügen");
    btnAddResource->setStyleSheet("QPushButton { padding: 8px; margin: 5px; }");
    btnAddProject->setStyleSheet("QPushButton { padding: 8px; margin: 5px; }");
    
    projectsList = new QTableWidget();
    projectsList->setColumnCount(3);
    projectsList->setHorizontalHeaderLabels({"Nr.", "Projekt", "Farbe"});
    projectsList->horizontalHeader()->setStretchLastSection(true);
    projectsList->setSelectionBehavior(QAbstractItemView::SelectRows);
    projectsList->setAlternatingRowColors(true);
    projectsList->setDragEnabled(true);
    projectsList->setDragDropMode(QAbstractItemView::DragOnly);
    
    sidebarLayout->addWidget(btnAddResource);
    sidebarLayout->addWidget(btnAddProject);
    sidebarLayout->addWidget(projectsList);
    
    calendarWidget = new ResourceCalendarWidget();
    scrollArea = new QScrollArea();
    scrollArea->setWidget(calendarWidget);
    scrollArea->setWidgetResizable(false);
    scrollArea->setHorizontalScrollBarPolicy(Qt::ScrollBarAsNeeded);
    scrollArea->setVerticalScrollBarPolicy(Qt::ScrollBarAsNeeded);
    
    contentLayout->addWidget(sidebarWidget);
    contentLayout->addWidget(scrollArea, 1);
    
    mainLayout->addWidget(headerWidget);
    mainLayout->addWidget(contentWidget, 1);
    
    connect(btnPrevious, &QPushButton::clicked, this, &ResourcesPage::navigatePrevious);
    connect(btnNext, &QPushButton::clicked, this, &ResourcesPage::navigateNext);
    connect(btnToday, &QPushButton::clicked, this, &ResourcesPage::navigateToday);
    connect(cmbViewMode, QOverload<int>::of(&QComboBox::currentIndexChanged), this, &ResourcesPage::changeViewMode);
    connect(btnAddResource, &QPushButton::clicked, this, &ResourcesPage::addResource);
    connect(btnAddProject, &QPushButton::clicked, this, &ResourcesPage::addProject);
}

void ResourcesPage::loadResources() {
    QSqlQuery query("SELECT * FROM resources ORDER BY name");
    calendarWidget->updateDisplay();
}

void ResourcesPage::loadProjects() {
    projectsList->setRowCount(0);
    QSqlQuery query("SELECT * FROM projects ORDER BY project_number, name");
    while (query.next()) {
        int row = projectsList->rowCount();
        projectsList->insertRow(row);
        
        auto* numberItem = new QTableWidgetItem(query.value("project_number").toString());
        numberItem->setData(Qt::UserRole, query.value("id").toInt());
        projectsList->setItem(row, 0, numberItem);
        
        auto* nameItem = new QTableWidgetItem(query.value("name").toString());
        nameItem->setData(Qt::UserRole, query.value("id").toInt());
        projectsList->setItem(row, 1, nameItem);
        
        auto* colorItem = new QTableWidgetItem();
        colorItem->setBackground(QColor(query.value("color").toString()));
        colorItem->setData(Qt::UserRole, query.value("id").toInt());
        projectsList->setItem(row, 2, colorItem);
    }
    projectsList->resizeColumnToContents(0);
}

void ResourcesPage::addResource() {
    bool ok;
    QString name = QInputDialog::getText(this, "Neuer Mitarbeiter", "Name:", QLineEdit::Normal, "", &ok);
    if (ok && !name.isEmpty()) {
        QString role = QInputDialog::getText(this, "Neuer Mitarbeiter", "Rolle/Position:", QLineEdit::Normal, "", &ok);
        if (ok) {
            QSqlQuery query;
            query.prepare("INSERT INTO resources (name, role, availability) VALUES (?, ?, 1.0)");
            query.addBindValue(name);
            query.addBindValue(role);
            if (query.exec()) {
                loadResources();
            }
        }
    }
}

void ResourcesPage::addProject() {
    bool ok;
    QString projectNumber = QInputDialog::getText(this, "Neues Projekt", "Projektnummer:", QLineEdit::Normal, "", &ok);
    if (ok && !projectNumber.isEmpty()) {
        QString name = QInputDialog::getText(this, "Neues Projekt", "Projektname:", QLineEdit::Normal, "", &ok);
        if (ok && !name.isEmpty()) {
            QColor color = QColorDialog::getColor(Qt::blue, this, "Projektfarbe wählen");
            if (color.isValid()) {
                QSqlQuery query;
                query.prepare("INSERT INTO projects (project_number, name, color) VALUES (?, ?, ?)");
                query.addBindValue(projectNumber);
                query.addBindValue(name);
                query.addBindValue(color.name());
                if (query.exec()) {
                    loadProjects();
                }
            }
        }
    }
}

void ResourcesPage::updateCalendar() {
    calendarWidget->setCurrentDate(currentDate);
    calendarWidget->updateDisplay();
    
    QString dateRange;
    switch (currentViewMode) {
        case Day:
            dateRange = currentDate.toString("dd.MM.yyyy");
            break;
        case Week: {
            int dayOfWeek = currentDate.dayOfWeek();
            QDate weekStart = currentDate.addDays(1 - dayOfWeek);
            QDate weekEnd = weekStart.addDays(6);
            dateRange = QString("%1 - %2").arg(weekStart.toString("dd.MM")).arg(weekEnd.toString("dd.MM.yyyy"));
            break;
        }
        case Month:
            dateRange = currentDate.toString("MMMM yyyy");
            break;
    }
    lblDateRange->setText(dateRange);
}

void ResourcesPage::navigatePrevious() {
    switch (currentViewMode) {
        case Day: currentDate = currentDate.addDays(-1); break;
        case Week: currentDate = currentDate.addDays(-7); break;
        case Month: currentDate = currentDate.addMonths(-1); break;
    }
    updateCalendar();
}

void ResourcesPage::navigateNext() {
    switch (currentViewMode) {
        case Day: currentDate = currentDate.addDays(1); break;
        case Week: currentDate = currentDate.addDays(7); break;
        case Month: currentDate = currentDate.addMonths(1); break;
    }
    updateCalendar();
}

void ResourcesPage::navigateToday() {
    currentDate = QDate::currentDate();
    updateCalendar();
}

void ResourcesPage::changeViewMode() {
    currentViewMode = static_cast<ViewMode>(cmbViewMode->currentIndex());
    calendarWidget->setViewMode(currentViewMode);
    updateCalendar();
}

ResourceCalendarWidget::ResourceCalendarWidget(QWidget* parent) 
    : QWidget(parent), viewMode(1), currentDate(QDate::currentDate()),
      cellWidth(100), cellHeight(60), headerHeight(50), sidebarWidth(200),
      draggedAllocation(nullptr) {
    setAcceptDrops(true);
    setMinimumSize(1200, 600);
}

void ResourceCalendarWidget::setViewMode(int mode) {
    viewMode = mode;
    update();
}

void ResourceCalendarWidget::setCurrentDate(const QDate& date) {
    currentDate = date;
    update();
}

void ResourceCalendarWidget::updateDisplay() {
    resources.clear();
    projects.clear();
    allocations.clear();
    
    QSqlQuery query("SELECT * FROM resources ORDER BY name");
    while (query.next()) {
        resources.append({
            query.value("id").toInt(),
            query.value("name").toString(),
            query.value("role").toString(),
            query.value("availability").toDouble()
        });
    }
    
    query.exec("SELECT * FROM projects ORDER BY project_number, name");
    while (query.next()) {
        projects.append({
            query.value("id").toInt(),
            query.value("project_number").toString(),
            query.value("name").toString(),
            QColor(query.value("color").toString())
        });
    }
    
    query.exec("SELECT * FROM resource_allocations");
    while (query.next()) {
        allocations.append({
            query.value("resource_id").toInt(),
            query.value("project_id").toInt(),
            query.value("date").toDate(),
            query.value("hours").toDouble()
        });
    }
    
    int cols = (viewMode == 0) ? 1 : (viewMode == 1) ? 7 : 30;
    setFixedSize(sidebarWidth + cols * cellWidth + 50, headerHeight + resources.size() * cellHeight + 50);
    update();
}

void ResourceCalendarWidget::paintEvent(QPaintEvent*) {
    QPainter painter(this);
    painter.setRenderHint(QPainter::Antialiasing);
    
    painter.fillRect(rect(), QColor("#ffffff"));
    
    drawHeader(painter);
    drawSidebar(painter);
    drawGrid(painter);
    drawAllocations(painter);
}

void ResourceCalendarWidget::drawHeader(QPainter& painter) {
    painter.fillRect(0, 0, width(), headerHeight, QColor("#34495e"));
    painter.setPen(Qt::white);
    painter.setFont(QFont("Arial", 10));
    
    int cols = (viewMode == 0) ? 1 : (viewMode == 1) ? 7 : 30;
    int dayOfWeek = currentDate.dayOfWeek();
    QDate startDate = (viewMode == 1) ? currentDate.addDays(1 - dayOfWeek) : currentDate;
    
    for (int col = 0; col < cols; ++col) {
        QDate date = startDate.addDays(col);
        QString text = (viewMode == 2) ? QString::number(date.day()) : date.toString("ddd dd.MM");
        
        QRect cellRect(sidebarWidth + col * cellWidth, 0, cellWidth, headerHeight);
        painter.drawText(cellRect, Qt::AlignCenter, text);
        
        if (date.dayOfWeek() >= 6) {
            painter.fillRect(cellRect, QColor(255, 255, 255, 30));
        }
    }
}

void ResourceCalendarWidget::drawSidebar(QPainter& painter) {
    painter.fillRect(0, 0, sidebarWidth, height(), QColor("#ecf0f1"));
    painter.setPen(Qt::black);
    painter.setFont(QFont("Arial", 10));
    
    for (int i = 0; i < resources.size(); ++i) {
        QRect cellRect(0, headerHeight + i * cellHeight, sidebarWidth, cellHeight);
        painter.fillRect(cellRect, (i % 2 == 0) ? QColor("#ecf0f1") : QColor("#d5dbdb"));
        
        painter.drawText(cellRect.adjusted(10, 5, -10, -cellHeight/2), 
                        Qt::AlignLeft | Qt::AlignVCenter, resources[i].name);
        painter.setPen(QColor("#7f8c8d"));
        painter.setFont(QFont("Arial", 9));
        painter.drawText(cellRect.adjusted(10, cellHeight/2 - 5, -10, -15), 
                        Qt::AlignLeft | Qt::AlignVCenter, resources[i].role);
        painter.setPen(Qt::black);
        painter.setFont(QFont("Arial", 10));
        
        double utilization = 0.8;
        QRect barRect(10, cellRect.bottom() - 8, sidebarWidth - 20, 4);
        painter.fillRect(barRect, QColor("#bdc3c7"));
        painter.fillRect(barRect.adjusted(0, 0, -(barRect.width() * (1 - utilization)), 0),
                        utilization > 0.9 ? QColor("#e74c3c") : QColor("#27ae60"));
    }
}

void ResourceCalendarWidget::drawGrid(QPainter& painter) {
    painter.setPen(QColor("#bdc3c7"));
    
    int cols = (viewMode == 0) ? 1 : (viewMode == 1) ? 7 : 30;
    int dayOfWeek = currentDate.dayOfWeek();
    QDate startDate = (viewMode == 1) ? currentDate.addDays(1 - dayOfWeek) : currentDate;
    
    for (int row = 0; row < resources.size(); ++row) {
        for (int col = 0; col < cols; ++col) {
            QRect cellRect = getCellRect(row, col);
            painter.drawRect(cellRect);
            
            if (col < cols && startDate.addDays(col).dayOfWeek() >= 6) {
                painter.fillRect(cellRect, QColor(200, 200, 200, 30));
            }
        }
    }
}

void ResourceCalendarWidget::drawAllocations(QPainter& painter) {
    for (const auto& allocation : allocations) {
        int resourceRow = -1;
        for (int i = 0; i < resources.size(); ++i) {
            if (resources[i].id == allocation.resourceId) {
                resourceRow = i;
                break;
            }
        }
        
        if (resourceRow == -1) continue;
        
        QColor projectColor("#3498db");
        QString projectDisplay = "Project";
        for (const auto& project : projects) {
            if (project.id == allocation.projectId) {
                projectColor = project.color;
                projectDisplay = project.projectNumber.isEmpty() ? 
                    project.name : 
                    QString("%1 - %2").arg(project.projectNumber).arg(project.name);
                break;
            }
        }
        
        int dayOfWeek = currentDate.dayOfWeek();
        QDate startDate = (viewMode == 1) ? currentDate.addDays(1 - dayOfWeek) : currentDate;
        int dayOffset = startDate.daysTo(allocation.date);
        
        if (dayOffset >= 0 && dayOffset < ((viewMode == 0) ? 1 : (viewMode == 1) ? 7 : 30)) {
            QRect cellRect = getCellRect(resourceRow, dayOffset);
            cellRect.adjust(2, 2, -2, -2);
            
            painter.fillRect(cellRect, projectColor);
            painter.setPen(Qt::white);
            painter.setFont(QFont("Arial", 9));
            painter.drawText(cellRect, Qt::AlignCenter, projectDisplay);
        }
    }
}

QRect ResourceCalendarWidget::getCellRect(int row, int col) {
    return QRect(sidebarWidth + col * cellWidth, headerHeight + row * cellHeight, cellWidth, cellHeight);
}

void ResourceCalendarWidget::mousePressEvent(QMouseEvent* event) {
    if (event->button() == Qt::LeftButton) {
        dragStartPosition = event->pos();
        
        // Check if clicking on an existing allocation
        int resourceRow = getResourceAtPos(event->pos());
        QDate date = getDateAtPos(event->pos());
        
        if (resourceRow >= 0 && date.isValid()) {
            for (auto& alloc : allocations) {
                if (alloc.resourceId == resources[resourceRow].id && alloc.date == date) {
                    // Found existing allocation - prepare for drag
                    draggedAllocation = &alloc;
                    break;
                }
            }
        }
    } else if (event->button() == Qt::RightButton) {
        // Right-click for context menu
        int resourceRow = getResourceAtPos(event->pos());
        QDate date = getDateAtPos(event->pos());
        
        if (resourceRow >= 0 && date.isValid()) {
            for (auto it = allocations.begin(); it != allocations.end(); ++it) {
                if (it->resourceId == resources[resourceRow].id && it->date == date) {
                    // Show context menu to delete
                    QMenu menu(this);
                    QAction* deleteAction = menu.addAction("Zuweisung löschen");
                    if (menu.exec(event->globalPosition().toPoint()) == deleteAction) {
                        QSqlQuery query;
                        query.prepare("DELETE FROM resource_allocations WHERE resource_id=? AND date=?");
                        query.addBindValue(it->resourceId);
                        query.addBindValue(it->date.toString(Qt::ISODate));
                        if (query.exec()) {
                            allocations.erase(it);
                            update();
                        }
                    }
                    break;
                }
            }
        }
    }
}

void ResourceCalendarWidget::mouseMoveEvent(QMouseEvent* event) {
    if (!(event->buttons() & Qt::LeftButton)) return;
    if ((event->pos() - dragStartPosition).manhattanLength() < 10) return;
    
    if (draggedAllocation) {
        auto* drag = new QDrag(this);
        auto* mimeData = new QMimeData;
        mimeData->setText(QString("move:%1:%2:%3")
            .arg(draggedAllocation->resourceId)
            .arg(draggedAllocation->projectId)
            .arg(draggedAllocation->date.toString(Qt::ISODate)));
        drag->setMimeData(mimeData);
        drag->exec(Qt::MoveAction);
    }
}

void ResourceCalendarWidget::mouseReleaseEvent(QMouseEvent*) {
    draggedAllocation = nullptr;
}

void ResourceCalendarWidget::dragEnterEvent(QDragEnterEvent* event) {
    if (event->mimeData()->hasText()) {
        event->acceptProposedAction();
    }
}

void ResourceCalendarWidget::dragMoveEvent(QDragMoveEvent* event) {
    if (event->mimeData()->hasText()) {
        event->acceptProposedAction();
    }
}

void ResourceCalendarWidget::dropEvent(QDropEvent* event) {
    if (!event->mimeData()->hasText()) return;
    
    int resourceRow = getResourceAtPos(event->position().toPoint());
    QDate targetDate = getDateAtPos(event->position().toPoint());
    
    if (resourceRow < 0 || !targetDate.isValid()) return;
    
    QString mimeText = event->mimeData()->text();
    
    if (mimeText.startsWith("move:")) {
        // Moving existing allocation
        QStringList parts = mimeText.split(":");
        if (parts.size() >= 4) {
            int oldResourceId = parts[1].toInt();
            int projectId = parts[2].toInt();
            QDate oldDate = QDate::fromString(parts[3], Qt::ISODate);
            
            // Delete old allocation
            QSqlQuery deleteQuery;
            deleteQuery.prepare("DELETE FROM resource_allocations WHERE resource_id=? AND project_id=? AND date=?");
            deleteQuery.addBindValue(oldResourceId);
            deleteQuery.addBindValue(projectId);
            deleteQuery.addBindValue(oldDate.toString(Qt::ISODate));
            deleteQuery.exec();
            
            // Create new allocation
            QSqlQuery insertQuery;
            insertQuery.prepare("INSERT OR REPLACE INTO resource_allocations (resource_id, project_id, date, hours) VALUES (?, ?, ?, 8.0)");
            insertQuery.addBindValue(resources[resourceRow].id);
            insertQuery.addBindValue(projectId);
            insertQuery.addBindValue(targetDate.toString(Qt::ISODate));
            
            if (insertQuery.exec()) {
                updateDisplay();
            }
        }
    } else if (event->source() != this) {
        // Dragging from project list
        auto* tableWidget = qobject_cast<QTableWidget*>(event->source());
        if (tableWidget) {
            auto selectedItems = tableWidget->selectedItems();
            if (!selectedItems.isEmpty()) {
                int projectId = selectedItems[0]->data(Qt::UserRole).toInt();
                
                // Check if we want to create a range
                if (event->modifiers() & Qt::ShiftModifier) {
                    // Shift pressed - create week allocation
                    for (int i = 0; i < 5; ++i) { // Mon-Fri
                        QDate date = targetDate.addDays(i);
                        if (date.dayOfWeek() <= 5) { // Weekdays only
                            QSqlQuery query;
                            query.prepare("INSERT OR REPLACE INTO resource_allocations (resource_id, project_id, date, hours) VALUES (?, ?, ?, 8.0)");
                            query.addBindValue(resources[resourceRow].id);
                            query.addBindValue(projectId);
                            query.addBindValue(date.toString(Qt::ISODate));
                            query.exec();
                        }
                    }
                } else {
                    // Single day allocation
                    QSqlQuery query;
                    query.prepare("INSERT OR REPLACE INTO resource_allocations (resource_id, project_id, date, hours) VALUES (?, ?, ?, 8.0)");
                    query.addBindValue(resources[resourceRow].id);
                    query.addBindValue(projectId);
                    query.addBindValue(targetDate.toString(Qt::ISODate));
                    
                    if (query.exec()) {
                        updateDisplay();
                    }
                }
                updateDisplay();
            }
        }
    }
    
    event->acceptProposedAction();
}

int ResourceCalendarWidget::getResourceAtPos(const QPoint& pos) {
    if (pos.y() < headerHeight) return -1;
    int row = (pos.y() - headerHeight) / cellHeight;
    return (row >= 0 && row < resources.size()) ? row : -1;
}

QDate ResourceCalendarWidget::getDateAtPos(const QPoint& pos) {
    if (pos.x() < sidebarWidth) return QDate();
    int col = (pos.x() - sidebarWidth) / cellWidth;
    int dayOfWeek = currentDate.dayOfWeek();
    QDate startDate = (viewMode == 1) ? currentDate.addDays(1 - dayOfWeek) : currentDate;
    return startDate.addDays(col);
}