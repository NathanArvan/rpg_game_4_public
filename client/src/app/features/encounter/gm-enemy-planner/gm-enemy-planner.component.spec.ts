import { ComponentFixture, TestBed } from '@angular/core/testing';

import { GmEnemyPlannerComponent } from './gm-enemy-planner.component';

describe('GmEnemyPlannerComponent', () => {
  let component: GmEnemyPlannerComponent;
  let fixture: ComponentFixture<GmEnemyPlannerComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [GmEnemyPlannerComponent]
    })
    .compileComponents();

    fixture = TestBed.createComponent(GmEnemyPlannerComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
